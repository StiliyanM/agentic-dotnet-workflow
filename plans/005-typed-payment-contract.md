# Plan 005-typed-payment-contract

`POST /payments` gets a typed contract: enums for currency, method and status, `required` non-nullable members, and a `Uri` redirect URL. Binding errors (missing field, unknown enum, numeric enum, wrong JSON type) become the same 400 validation problem as validator errors. The route, the 201 shape and the amount rules do not change.

## 1. Files

Domain (`src/ApmPlayground.Domain/Payments/`)
- `Currency.cs`: new enum `Currency { EUR, GBP, USD }`.
- `SupportedCurrencies.cs`: delete. The enum is the supported set.
- `Payment.cs`: constructor parameter and `Currency` property change from `string` to `Currency`.

Application (`src/ApmPlayground.Application/Payments/`)
- `CreatePaymentRequest.cs`: record with `required` non-nullable init members (see section 2).
- `CreatePaymentResponse.cs`: `RedirectUrl` becomes `Uri`, `Status` becomes `PaymentStatus`.
- `CreatePaymentValidator.cs`: keep only the amount rules, without `NotNull`. Remove the currency and method rules.
- `CreatePaymentUseCase.cs`: remove the `TryParse`. Build the `Payment` from the typed request. Build the redirect `Uri` with a private static method that maps the method to its lowercase segment.
- `PaymentMethodNames.cs`: delete.
- `CreatePaymentResult.cs`, `IPaymentRepository.cs`, `ApplicationServiceCollectionExtensions.cs`: no change.

Infrastructure
- `Persistence/PaymentConfiguration.cs`: `Currency` gets `HasConversion<string>().HasMaxLength(3).IsRequired()`. The column stays varchar(3) with values "EUR", "GBP" or "USD".

Api
- `Program.cs`: configure the JSON options, `ThrowOnBadRequest`, problem details, the exception handler, and `app.UseExceptionHandler()` (see section 2).
- `RequestBodyExceptionHandler.cs` (new, namespace `ApmPlayground.Api`): maps request body binding failures to a 400 problem.
- `Payments/PaymentEndpoints.cs`: no change.

Tests
- `tests/ApmPlayground.UnitTests/Payments/PaymentTests.cs`: use `Currency` values.
- `tests/ApmPlayground.UnitTests/Payments/CreatePaymentValidatorTests.cs`: typed requests. Delete the tests for rules that no longer exist.
- `tests/ApmPlayground.UnitTests/Payments/CreatePaymentUseCaseTests.cs`: typed requests and typed response asserts.
- `tests/ApmPlayground.UnitTests/Payments/FakePaymentRepository.cs`: no change.
- `tests/ApmPlayground.IntegrationTests/CreatePaymentTests.cs`: change the tests and add new tests (section 3).

## 2. Public types and signatures

### Domain, `ApmPlayground.Domain.Payments`
```csharp
public enum Currency { EUR, GBP, USD }                    // ISO 4217 codes; member name == stored and wire value
public enum PaymentMethod { Ideal, Klarna }               // unchanged
public enum PaymentStatus { Pending }                     // unchanged

public class Payment
{
    public const int AmountPrecision = 18;
    public const int AmountScale = 2;
    public const decimal MaxAmount = 9999999999999999.99m;
    public Payment(decimal amount, Currency currency, PaymentMethod method);
    public Guid Id { get; private set; }
    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
```

### Application, `ApmPlayground.Application.Payments`
```csharp
public record CreatePaymentRequest
{
    public required decimal Amount { get; init; }
    public required Currency Currency { get; init; }
    public required PaymentMethod Method { get; init; }
}

public record CreatePaymentResponse(Guid PaymentId, Uri RedirectUrl, PaymentStatus Status);

public sealed class CreatePaymentValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentValidator();
}

public sealed class CreatePaymentUseCase(IValidator<CreatePaymentRequest> validator, IPaymentRepository repository)
{
    public Task<CreatePaymentResult> ExecuteAsync(CreatePaymentRequest request, CancellationToken cancellationToken);
}
```
Validator: `RuleLevelCascadeMode = CascadeMode.Stop`. One rule, `RuleFor(r => r.Amount)`, key `amount` (`OverridePropertyName`), in this order:
`GreaterThan(0)` "Amount must be greater than 0."; `LessThanOrEqualTo(Payment.MaxAmount)` "Amount is too large."; `Must(a => decimal.Round(a, Payment.AmountScale) == a)` "Amount must have at most 2 decimal places.".

Use case: `ThrowIfNull(request)`; validate; if not valid return `Invalid(validationResult.ToDictionary())`; else `new Payment(request.Amount, request.Currency, request.Method)`, `AddAsync`, return `Success(new CreatePaymentResponse(payment.Id, new Uri($"{RedirectBaseUrl}/{segment}/{payment.Id:D}"), payment.Status))`. `segment` comes from a private static method with a switch: `Ideal => "ideal"`, `Klarna => "klarna"`, `_ => throw new ArgumentOutOfRangeException(...)`.

### Api
`Program.cs` adds, before `Build()`:
```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)));
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RequestBodyExceptionHandler>();
```
and after `Build()`, before the endpoints: `app.UseExceptionHandler();`.

```csharp
namespace ApmPlayground.Api;

public sealed class RequestBodyExceptionHandler(IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken);
}
```
Behavior of `TryHandleAsync`:
1. If `exception` is not `BadHttpRequestException`, return `false`.
2. Get the field errors (step 3). If there are any, execute `TypedResults.ValidationProblem(errors)` on the context. Otherwise execute `TypedResults.Problem(statusCode: badRequest.StatusCode)`. Return `true`.
3. Field errors. They exist only when `badRequest.InnerException` is a `JsonException json`, and `json.InnerException` is NOT a `JsonException` (an inner `JsonException` is the reader's syntax error, which means malformed JSON):
   - Contract properties: the request type is `httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint?.Metadata.GetMetadata<IAcceptsMetadata>()?.RequestType` (the exception handler middleware clears `GetEndpoint()`, so use the feature). The properties are `jsonOptions.Value.SerializerOptions.GetTypeInfo(requestType).Properties`. If there is no request type, there are no field errors.
   - If `json.Path` starts with `"$."`: the segment is the text after `"$."` up to the first `.` or `[`. Find the property whose `Name` equals the segment with `OrdinalIgnoreCase`. If found: `errors[property.Name] = ["{Field} has an invalid value."]`.
   - Otherwise (path `"$"` or null): for each property with `IsRequired` whose quoted name `'{Name}'` is in `json.Message` (ordinal): `errors[property.Name] = ["{Field} is required."]`.
   - `{Field}` is the JSON name with the first letter in upper case (`amount` -> `Amount`).

### HTTP: `POST /payments`
Request `application/json`:
```json
{ "amount": 10.50, "currency": "EUR", "method": "ideal" }
```
- `amount`: JSON number, required. `currency`: string `EUR|GBP|USD`, any case, required. `method`: string `ideal|klarna`, any case, required. Unknown extra properties are ignored.

Responses:
- 201 `application/json`, no Location header: `{ "paymentId": "<guid>", "redirectUrl": "https://pay.example.com/{ideal|klarna}/{paymentId:D}", "status": "Pending" }`. `paymentId` is a Guid string, `redirectUrl` is the `Uri` original string, `status` is the enum name.
- 400 `application/problem+json` validation problem (`title` "One or more validation errors occurred.", `status` 400, `errors`). Messages:

| Case | Key | Message |
|---|---|---|
| amount <= 0 | `amount` | `Amount must be greater than 0.` |
| amount > max | `amount` | `Amount is too large.` |
| amount > 2 decimals | `amount` | `Amount must have at most 2 decimal places.` |
| field missing | `amount` / `currency` / `method` | `Amount is required.` / `Currency is required.` / `Method is required.` |
| unknown enum string, numeric enum, wrong JSON type, `null` | the field | `Amount has an invalid value.` / `Currency has an invalid value.` / `Method has an invalid value.` |

- 400 `application/problem+json` plain problem (`status` 400, no `errors` member) for malformed JSON, an empty body, or a body that is not a JSON object.

## 3. Tests

Unit (`ApmPlayground.UnitTests`)
- `PaymentTests.Constructor_SetsPendingStatusAndNewId`, `Constructor_TwoPayments_HaveDifferentIds`: changed. Currency comes from `_fixture.Create<Currency>()`.
- `CreatePaymentValidatorTests`, with `ValidRequest()` building a typed request (random amount + 0.99m, random `Currency`, random `PaymentMethod`):
  - `Validate_ValidRequest_ReturnsNoErrors`: changed. InlineData `(PaymentMethod.Ideal, Currency.EUR, "10.50")`, `(Klarna, GBP, "0.01")`, `(Klarna, USD, "1000")`, `(Ideal, EUR, "9999999999999999.99")`.
  - `Validate_AmountNotPositive_ReturnsAmountError`, `Validate_AmountAboveMaximum_ReturnsAmountError`, `Validate_AmountWithMoreThanTwoDecimals_ReturnsAmountError`: unchanged bodies (they use `ValidRequest() with { Amount = ... }`).
  - Deleted: `Validate_AmountMissing_ReturnsAmountError`, `Validate_UnsupportedCurrency_ReturnsCurrencyError`, `Validate_CurrencyMissing_ReturnsCurrencyError`, `Validate_UnknownMethod_ReturnsMethodError`, `Validate_MethodMissing_ReturnsMethodError`, `Validate_AllFieldsInvalid_ReturnsErrorForEachField`. Their rules are now enforced by the type system and the JSON binding; the integration tests prove the HTTP behavior.
- `CreatePaymentUseCaseTests`:
  - `ExecuteAsync_ValidRequest_ReturnsPendingResponseWithRedirectUrl`: changed to Theory `(PaymentMethod.Ideal, "ideal")`, `(PaymentMethod.Klarna, "klarna")`. Asserts `Status == PaymentStatus.Pending` and `RedirectUrl == new Uri($"https://pay.example.com/{segment}/{PaymentId:D}")`.
  - `ExecuteAsync_ValidRequest_AddsPaymentToRepository`: changed. Method `PaymentMethod.Klarna`; asserts the typed currency and method on the saved payment.
  - `ExecuteAsync_InvalidRequest_ReturnsErrorsAndDoesNotAddPayment`: unchanged body.

Integration (`CreatePaymentTests`). Add a static `JsonSerializerOptions(JsonSerializerDefaults.Web)` with `JsonStringEnumConverter` for reading `CreatePaymentResponse`. Add a helper that posts a raw JSON string (`StringContent`, `application/json`). Add a helper `RawBody(string field, string jsonValue)` that returns the valid body `{"amount":10.50,"currency":"EUR","method":"ideal"}` with that field's value replaced, and `BodyWithout(string field)` that omits it.
- `CreatePayment_ValidRequest_Returns201WithPendingStatusAndRedirectUrl` (ideal, klarna): changed. Also parses the raw JSON and asserts `status` is the string `"Pending"` and `redirectUrl` is the string `https://pay.example.com/{method}/{id:D}`.
- `CreatePayment_ValidRequest_PersistsPayment`: changed to InlineData `("ideal", PaymentMethod.Ideal, "EUR", Currency.EUR)`, `("klarna", PaymentMethod.Klarna, "USD", Currency.USD)`; asserts the typed currency.
- `CreatePayment_TwoRequests_ReturnDifferentPaymentIds`, `CreatePayment_AmountZero_...`, `CreatePayment_NegativeAmount_...`, `CreatePayment_AmountAboveMaximum_...`: unchanged.
- `CreatePayment_MalformedJson_Returns400`: changed. Also asserts `application/problem+json` and that there is no `errors` member.
- Deleted (replaced by the theory below): `CreatePayment_UnsupportedCurrency_Returns400WithCurrencyError`, `CreatePayment_UnknownMethod_Returns400WithMethodError`.
- New `CreatePayment_MethodAndCurrencyInAnyCase_Returns201` (Theory: `("IDEAL","eur")`, `("Ideal","Eur")`, `("KLARNA","gbp")`, `("Klarna","Usd")`): 201, and the redirect URL uses the lowercase method (`ideal`/`klarna`). Proves case-insensitive reading and the lowercase URL segment.
- New `CreatePayment_FieldWithInvalidValue_Returns400WithInvalidValueError` (Theory field, jsonValue): `("currency","\"JPY\"")`, `("currency","0")`, `("currency","null")`, `("method","\"paypal\"")`, `("method","1")`, `("method","null")`, `("amount","\"abc\"")`, `("amount","true")`, `("amount","null")`. Validation problem with one error under `field`: `"{Field} has an invalid value."`.
- New `CreatePayment_MissingField_Returns400WithRequiredError` (Theory: `amount`, `currency`, `method`): validation problem with one error under that key: `"{Field} is required."`.
- New `CreatePayment_EmptyObject_Returns400WithErrorForEachField`: body `{}` gives three keys, each with `"{Field} is required."`.
- New `CreatePayment_PropertyNameInOtherCase_ErrorKeyIsCamelCase`: body `{"amount":10.50,"currency":"EUR","Method":"paypal"}` gives the error under `method`.
- New `CreatePayment_EmptyBody_Returns400`: an empty `application/json` body gives 400 `application/problem+json`.

`HealthTests`: unchanged.

## 4. Edge cases

| Edge case | Test |
|---|---|
| Case-insensitive method and currency | `CreatePayment_MethodAndCurrencyInAnyCase_Returns201` |
| Redirect URL keeps the lowercase method | `CreatePayment_MethodAndCurrencyInAnyCase_Returns201`, `..._Returns201WithPendingStatusAndRedirectUrl` |
| Status serialized as "Pending", not 0 | `..._Returns201WithPendingStatusAndRedirectUrl` |
| Unknown currency / method | `CreatePayment_FieldWithInvalidValue_...` (`JPY`, `paypal`) |
| Numeric enum value | `CreatePayment_FieldWithInvalidValue_...` (`0`, `1`) |
| Wrong JSON type for amount | `CreatePayment_FieldWithInvalidValue_...` (`"abc"`, `true`) |
| Explicit `null` for a field | `CreatePayment_FieldWithInvalidValue_...` (`null` rows) |
| One missing field | `CreatePayment_MissingField_...` |
| All fields missing | `CreatePayment_EmptyObject_...` |
| Property name sent in another case | `CreatePayment_PropertyNameInOtherCase_ErrorKeyIsCamelCase` |
| Malformed JSON | `CreatePayment_MalformedJson_Returns400` |
| Empty body | `CreatePayment_EmptyBody_Returns400` |
| Amount rules from spec 001 | validator amount tests, `CreatePayment_AmountZero/Negative/AboveMaximum_...` |
| Currency stored as ISO code | `CreatePayment_ValidRequest_PersistsPayment` (enum read back from the "EUR"/"USD" column) |

## 5. Pattern
None. `RequestBodyExceptionHandler` is the framework's `IExceptionHandler` extension point, not an added pattern.

## 6. Decisions
- **D1 Currency enum.** `Currency { EUR, GBP, USD }` in Domain. Upper-case members are the ISO codes, so `HasConversion<string>()` stores "EUR" and no mapping code is needed. `SupportedCurrencies` is deleted. `Payment.Currency` is `Currency`. The column stays `varchar(3)` required, so existing rows still read.
- **D2 JSON options (Api only).** `ConfigureHttpJsonOptions` adds `new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)`. The converter reads enum names case-insensitively, and it writes the C# member name. So status stays `"Pending"`. Property names stay camelCase from the Web defaults, and property-name matching is case-insensitive. `TypedResults.Json` uses these options from DI. The default `NumberHandling` of the Web defaults (`AllowReadingFromString`) is kept, so `"amount": "10.50"` is accepted. No extra config is added (YAGNI).
- **D3 Binding failures.** By default, minimal APIs set 400 with no body when the body fails to deserialize. That gives no field key. So `RouteHandlerOptions.ThrowOnBadRequest = true` (in every environment, not only Development) makes the framework throw `BadHttpRequestException` with the `JsonException` as the inner exception. `UseExceptionHandler()` with `AddProblemDetails()` and `RequestBodyExceptionHandler` turns it into the problem described in section 2. Our handler runs before the Development exception page, because `UseExceptionHandler` is registered later in the pipeline.
- **D4 Finding the field.** For a value that cannot be converted (unknown enum, numeric enum, wrong type, null), `JsonException.Path` is `$.<name>`, and the name is matched to the contract property, so the key is always the contract's camelCase name. For a missing `required` member, the path is `$`, and System.Text.Json lists the missing JSON names in quotes in the message. Only the names of the contract's required properties are searched in the message, so no free-text parsing is needed. The contract comes from the endpoint's `IAcceptsMetadata.RequestType`, through `IExceptionHandlerFeature.Endpoint`.
- **D5 Malformed JSON.** A syntax error has the reader's exception (a `JsonException`) as its inner exception. That gives a plain 400 problem with no `errors`. An empty body, or other `BadHttpRequestException`s, give `TypedResults.Problem(statusCode: exception.StatusCode)`.
- **D6 Messages.** The binding messages are generic per field: "{Field} is required." (the same text as spec 001 for missing fields) and "{Field} has an invalid value.". The old texts "Currency 'X' is not supported." and "Method must be 'ideal' or 'klarna'." are dropped, because the handler does not get the rejected value.
- **D7 Removed code (YAGNI).** `PaymentMethodNames`, the double parse in the use case, the amount `NotNull` rule, and all currency and method validator rules are removed. With non-nullable enums they cannot fail. No `IsInEnum` rule is added, because HTTP cannot produce an undefined value.
- **D8 Redirect segment.** A private static switch in `CreatePaymentUseCase` maps `Ideal`/`Klarna` to `"ideal"`/`"klarna"`. It is not `ToString().ToLowerInvariant()`, because that would trigger CA1308 with `TreatWarningsAsErrors`.
- **D9 Response types.** `PaymentId` is `Guid`, `RedirectUrl` is `Uri` (STJ writes the original string), and `Status` is `PaymentStatus` (written as its name by the converter from D2).
- **D10 Request shape.** `CreatePaymentRequest` is a record with `required` init properties, not a positional record, because `required` cannot be put on positional parameters. STJ enforces `required` members on deserialization (a missing member throws a `JsonException`).
- **D11 Handler visibility.** `RequestBodyExceptionHandler` is `public sealed` (CA1812, as in plan 004 D9). It calls `ArgumentNullException.ThrowIfNull` on `httpContext` and `exception` (CA1062).
- **D12 Unit tests.** The handler lives in Api, and unit tests may not reference Api. So the binding behavior is proven only by the integration tests.
- **D13 Strict enum converter** (added by the orchestrator in loop 2, from a reviewer finding confirmed by tests). `JsonStringEnumConverter` accepts comma-separated names for non-[Flags] enums ("GBP, USD" becomes undefined 3, "ideal, klarna" becomes Klarna). Api uses `StrictEnumConverterFactory` instead: one exact member name, any case. Anything else is a `JsonException`, which gives "X has an invalid value.".
- **D14 Undefined enum values in Application** (loop 2). The validator has `IsInEnum` rules with the same messages as the JSON handler, for callers that do not use HTTP. The use case builds the redirect URL before it saves.
