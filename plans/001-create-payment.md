# Plan 001-create-payment

## 1. Files

- `src/ApmPlayground.Api/Payments/PaymentMethod.cs` (add): enum of the supported methods.
- `src/ApmPlayground.Api/Payments/PaymentStatus.cs` (add): enum of payment statuses (only `Pending` now).
- `src/ApmPlayground.Api/Payments/Payment.cs` (add): EF Core entity.
- `src/ApmPlayground.Api/Payments/PaymentMethodNames.cs` (add): maps the wire names "ideal"/"klarna" to `PaymentMethod` and back.
- `src/ApmPlayground.Api/Payments/SupportedCurrencies.cs` (add): the list of supported currency codes.
- `src/ApmPlayground.Api/Payments/CreatePaymentRequest.cs` (add): request DTO.
- `src/ApmPlayground.Api/Payments/CreatePaymentResponse.cs` (add): response DTO.
- `src/ApmPlayground.Api/Payments/CreatePaymentValidator.cs` (add): validates the request, returns errors per field.
- `src/ApmPlayground.Api/Payments/PaymentEndpoints.cs` (add): `MapPaymentEndpoints` extension with the `POST /payments` handler.
- `src/ApmPlayground.Api/Data/AppDbContext.cs` (change): add `DbSet<Payment> Payments` and model config in `OnModelCreating`.
- `src/ApmPlayground.Api/Program.cs` (change): call `db.Database.EnsureCreated()` in a scope after `Build()`, and call `app.MapPaymentEndpoints()`.
- `tests/ApmPlayground.UnitTests/Payments/CreatePaymentValidatorTests.cs` (add).
- `tests/ApmPlayground.UnitTests/Payments/PaymentTests.cs` (add).
- `tests/ApmPlayground.IntegrationTests/CreatePaymentTests.cs` (add): uses `[Collection(ApiCollection.Name)]` and `ApiFactory`.

## 2. Public types and signatures

Namespace for all new src types: `ApmPlayground.Api.Payments`.

```csharp
public enum PaymentMethod { Ideal, Klarna }

public enum PaymentStatus { Pending }

public class Payment
{
    public Payment(decimal amount, string currency, PaymentMethod method); // Id = Guid.CreateVersion7(), Status = Pending, CreatedAt = DateTimeOffset.UtcNow
    public Guid Id { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public static class PaymentMethodNames
{
    public static bool TryParse(string? value, out PaymentMethod method); // exact, case-sensitive: "ideal" -> Ideal, "klarna" -> Klarna
    public static string ToName(PaymentMethod method);                     // Ideal -> "ideal", Klarna -> "klarna"
}

public static class SupportedCurrencies
{
    public static IReadOnlySet<string> All { get; }   // "EUR", "GBP", "USD"
    public static bool IsSupported(string? code);     // exact, case-sensitive, ordinal; null -> false
}

public record CreatePaymentRequest(decimal? Amount, string? Currency, string? Method);

public record CreatePaymentResponse(Guid PaymentId, string RedirectUrl, string Status);

public static class CreatePaymentValidator
{
    // Returns an empty dictionary when the request is valid.
    // Keys: "amount", "currency", "method" (only the keys with errors). Each value has one message.
    public static Dictionary<string, string[]> Validate(CreatePaymentRequest request);
}

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app);
}
```

`AppDbContext` gets `public DbSet<Payment> Payments => Set<Payment>();`.

Error messages (exact):
- `amount`: missing -> "Amount is required."; `<= 0` -> "Amount must be greater than 0."; more than 2 decimal places -> "Amount must have at most 2 decimal places."
- `currency`: missing/empty -> "Currency is required."; not supported -> "Currency 'X' is not supported." (X = the sent value)
- `method`: missing/empty -> "Method is required."; unknown -> "Method must be 'ideal' or 'klarna'."

### HTTP: `POST /payments`

Request body (JSON, camelCase):
```json
{ "amount": 10.50, "currency": "EUR", "method": "ideal" }
```

Responses:
- `201 Created`, `application/json`, no `Location` header:
  ```json
  { "paymentId": "<guid>", "redirectUrl": "https://pay.example.com/ideal/<guid>", "status": "Pending" }
  ```
  `redirectUrl` = `https://pay.example.com/{methodName}/{paymentId}` where `methodName` is "ideal" or "klarna" and `paymentId` is the Guid in default "D" format (lowercase). The payment row is saved before the response.
- `400 Bad Request`, `application/problem+json`, validation problem (`TypedResults.ValidationProblem`):
  ```json
  { "title": "One or more validation errors occurred.", "status": 400, "errors": { "amount": ["Amount must be greater than 0."] } }
  ```
- `400 Bad Request` for a malformed JSON body or a body that cannot bind (framework default; body shape not specified).

## 3. Tests

Unit (`CreatePaymentValidatorTests`):
- `Validate_ValidRequest_ReturnsNoErrors` (Theory: ideal/EUR/10.50, klarna/GBP/0.01, klarna/USD/1000): valid input gives an empty dictionary.
- `Validate_AmountNotPositive_ReturnsAmountError` (Theory: 0, -0.01, -100): only key `amount`.
- `Validate_AmountMissing_ReturnsAmountError`: null amount gives `amount` error.
- `Validate_AmountWithMoreThanTwoDecimals_ReturnsAmountError` (Theory: 0.001, 10.123).
- `Validate_UnsupportedCurrency_ReturnsCurrencyError` (Theory: "JPY", "eur", "EURO").
- `Validate_CurrencyMissing_ReturnsCurrencyError` (Theory: null, "").
- `Validate_UnknownMethod_ReturnsMethodError` (Theory: "paypal", "IDEAL", "Klarna").
- `Validate_MethodMissing_ReturnsMethodError` (Theory: null, "").
- `Validate_AllFieldsInvalid_ReturnsErrorForEachField`: keys `amount`, `currency`, `method` all present.

Unit (`PaymentTests`):
- `Constructor_SetsPendingStatusAndNewId`: Status is Pending, Id is not empty, fields equal the inputs.
- `Constructor_TwoPayments_HaveDifferentIds`.

Integration (`CreatePaymentTests`):
- `CreatePayment_ValidRequest_Returns201WithPendingStatusAndRedirectUrl`: 201, non-empty paymentId, status "Pending", redirectUrl matches the format.
- `CreatePayment_ValidRequest_PersistsPayment`: load the row through `AppDbContext` (scope from `factory.Services`); amount, currency, method, status Pending match.
- `CreatePayment_TwoRequests_ReturnDifferentPaymentIds`.
- `CreatePayment_AmountZero_Returns400WithAmountError`: 400, problem+json, `errors.amount` present.
- `CreatePayment_NegativeAmount_Returns400WithAmountError`.
- `CreatePayment_UnsupportedCurrency_Returns400WithCurrencyError`.
- `CreatePayment_UnknownMethod_Returns400WithMethodError`.
- `CreatePayment_MalformedJson_Returns400`.

## 4. Edge cases

| Edge case | Test |
|---|---|
| amount = 0 | `Validate_AmountNotPositive_ReturnsAmountError`, `CreatePayment_AmountZero_Returns400WithAmountError` |
| amount negative | `Validate_AmountNotPositive_ReturnsAmountError`, `CreatePayment_NegativeAmount_Returns400WithAmountError` |
| smallest valid amount 0.01 | `Validate_ValidRequest_ReturnsNoErrors` |
| amount missing | `Validate_AmountMissing_ReturnsAmountError` |
| amount with > 2 decimals | `Validate_AmountWithMoreThanTwoDecimals_ReturnsAmountError` |
| unsupported currency | `Validate_UnsupportedCurrency_ReturnsCurrencyError`, `CreatePayment_UnsupportedCurrency_Returns400WithCurrencyError` |
| lowercase currency | `Validate_UnsupportedCurrency_ReturnsCurrencyError` ("eur") |
| currency missing/empty | `Validate_CurrencyMissing_ReturnsCurrencyError` |
| unknown method / wrong case | `Validate_UnknownMethod_ReturnsMethodError`, `CreatePayment_UnknownMethod_Returns400WithMethodError` |
| method missing/empty | `Validate_MethodMissing_ReturnsMethodError` |
| several invalid fields at once | `Validate_AllFieldsInvalid_ReturnsErrorForEachField` |
| malformed JSON | `CreatePayment_MalformedJson_Returns400` |
| each payment gets a unique id | `Constructor_TwoPayments_HaveDifferentIds`, `CreatePayment_TwoRequests_ReturnDifferentPaymentIds` |

## 5. Pattern

None. A static validator and a minimal API handler are enough.

## 6. Decisions

- **Supported currencies**: EUR, GBP, USD. Exact uppercase ISO 4217 codes; "eur" is rejected (no normalization). No method/currency compatibility check (e.g. iDEAL only EUR) because the spec does not ask for it.
- **Method values**: exactly "ideal" or "klarna", case-sensitive. Stored as the enum `PaymentMethod`, saved as a string column (`HasConversion<string>()`).
- **Status**: enum `PaymentStatus { Pending }`, saved as a string column (`HasConversion<string>()`), returned as the string "Pending". Specs 002/003 will add `Succeeded`/`Failed` to this enum; the names do not conflict.
- **Amount type and precision**: `decimal`, column `numeric(18,2)` (`HasPrecision(18, 2)`). More than 2 decimal places is rejected with 400 so the database does not round silently. No upper limit (YAGNI).
- **Currency column**: `HasMaxLength(3)`, required.
- **Request binding**: request fields are nullable so a missing field gives a clear validation error instead of a default value.
- **Validation**: hand-written `CreatePaymentValidator` (no FluentValidation, no DataAnnotations). All field errors are returned together.
- **Error shape**: RFC 7807 validation problem via `TypedResults.ValidationProblem`, `errors` keyed by camelCase field name.
- **Success status code**: 201 Created without a `Location` header, because there is no GET endpoint yet.
- **redirectUrl**: `https://pay.example.com/{method}/{paymentId}`, built from a constant in `PaymentEndpoints`. It is not stored; it is derived. No configuration setting (YAGNI).
- **Payment id**: `Guid.CreateVersion7()`, generated in the `Payment` constructor (not by the database).
- **Schema creation**: `EnsureCreated()` in a DI scope in `Program.cs` right after `builder.Build()`. Each integration test run uses a fresh container, so this is enough. No migrations now. (Known limit: `EnsureCreated` does not add tables to an existing database; a later spec that adds tables works in tests because the container is fresh.)
- **Table name**: `Payments` (default from the `DbSet`).
- **Analyzers**: new public types are in the Api project and must build clean with `TreatWarningsAsErrors` and `latest-recommended`; the implementer fixes any analyzer warning without suppressions where possible.
- **Maximum amount** (added by the orchestrator in loop 2, from a reviewer finding): 9999999999999999.99, the largest value of `numeric(18,2)`. A larger amount gives 400 with the `amount` error "Amount is too large." The column and the validator use the same constants in `Payment`.
