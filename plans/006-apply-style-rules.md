# Plan 006-apply-style-rules

Refactor only. No HTTP behavior change, no stored data change. The build must pass with `TreatWarningsAsErrors` and no warnings.

The planner could not run the build. The only known build error is IDE0290 on `Payment.cs`. This plan is based on reading every file. The implementer runs `dotnet build --no-incremental` after the changes and fixes any other analyzer warning in the same style.

## 1. Files

| File | Project | Change |
|---|---|---|
| `src/ApmPlayground.Domain/Payments/Payment.cs` | Domain | `sealed`, primary constructor, property initializers (see section 2). Fixes IDE0290. |
| `src/ApmPlayground.Domain/Payments/Currency.cs` | Domain | Members `EUR, GBP, USD` become `Eur, Gbp, Usd`. Change the comment because the member name is no longer the stored value (see section 2). |
| `src/ApmPlayground.Application/Payments/CreatePaymentRequest.cs` | Application | `public record` becomes `public sealed record`. |
| `src/ApmPlayground.Application/Payments/CreatePaymentResponse.cs` | Application | `public record` becomes `public sealed record`. |
| `src/ApmPlayground.Infrastructure/Persistence/AppDbContext.cs` | Infrastructure | `public class` becomes `public sealed class`. |
| `src/ApmPlayground.Infrastructure/Persistence/PaymentConfiguration.cs` | Infrastructure | Explicit value conversion for `Currency`: store the upper-case ISO code (see section 2). |
| `tests/ApmPlayground.IntegrationTests/Infrastructure/ApiFactory.cs` | IntegrationTests | `sealed`. |
| `tests/ApmPlayground.IntegrationTests/Infrastructure/ApiCollection.cs` | IntegrationTests | `sealed`. |
| `tests/ApmPlayground.IntegrationTests/HealthTests.cs` | IntegrationTests | `sealed`. |
| `tests/ApmPlayground.IntegrationTests/CreatePaymentTests.cs` | IntegrationTests | `sealed`. Use `Currency.Eur/Gbp/Usd`. Use raw string literals for the JSON bodies. Add one new test (section 3). |
| `tests/ApmPlayground.UnitTests/Payments/CreatePaymentUseCaseTests.cs` | UnitTests | `sealed`. |
| `tests/ApmPlayground.UnitTests/Payments/CreatePaymentValidatorTests.cs` | UnitTests | `sealed`. Use `Currency.Eur/Gbp/Usd` in `InlineData`. |
| `tests/ApmPlayground.UnitTests/Payments/PaymentTests.cs` | UnitTests | `sealed`. |

Files with no change. Each one already follows the rules. None of them uses `!`, has a backing field that the `field` keyword could replace, or has a one-expression member that is not expression-bodied:
`PaymentStatus.cs`, `PaymentMethod.cs`, `CreatePaymentUseCase.cs`, `CreatePaymentValidator.cs`, `CreatePaymentResult.cs`, `IPaymentRepository.cs`, `ApplicationServiceCollectionExtensions.cs`, `PaymentRepository.cs`, `InfrastructureServiceCollectionExtensions.cs`, `Program.cs`, `PaymentEndpoints.cs`, `RequestBodyExceptionHandler.cs`, `StrictEnumConverter.cs`, `StrictEnumConverterFactory.cs`, `FakePaymentRepository.cs`.

## 2. Public types and signatures

### Payment (Domain), exact shape after the change

```csharp
namespace ApmPlayground.Domain.Payments;

public sealed class Payment(decimal amount, Currency currency, PaymentMethod method)
{
    // The amount column is numeric(AmountPrecision, AmountScale); MaxAmount is the largest value it can hold.
    public const int AmountPrecision = 18;
    public const int AmountScale = 2;
    public const decimal MaxAmount = 9999999999999999.99m;

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public decimal Amount { get; private set; } = amount;

    public Currency Currency { get; private set; } = currency;

    public PaymentMethod Method { get; private set; } = method;

    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
```

- Use the parameters only in the property initializers. Do not use them in any member body. If a member body uses a parameter, the compiler captures it in a hidden field (CS9124), and the state is stored twice.
- EF Core constructor binding: the primary constructor is the only public constructor. EF matches the parameters `amount`, `currency` and `method` to the properties `Amount`, `Currency` and `Method` by name (case-insensitive), so EF calls this constructor when it loads a row. The constructor runs the initializers. They give `Id`, `Status` and `CreatedAt` temporary values. EF then sets `Id`, `Status` and `CreatedAt` from the row, through their backing fields or `private set`, and overwrites the temporary values. This works the same way as the current explicit constructor. Keep `private set` on all properties.
- The public API stays the same: `new Payment(decimal amount, Currency currency, PaymentMethod method)` and the same properties and constants.

### Currency (Domain)

```csharp
namespace ApmPlayground.Domain.Payments;

// ISO 4217 currencies. Infrastructure stores the upper-case ISO code; JSON input matches the code ignoring case.
public enum Currency
{
    Eur,
    Gbp,
    Usd,
}
```

### Contracts (Application), signatures do not change

```csharp
public sealed record CreatePaymentRequest
{
    public required decimal Amount { get; init; }
    public required Currency Currency { get; init; }
    public required PaymentMethod Method { get; init; }
}

public sealed record CreatePaymentResponse(Guid PaymentId, Uri RedirectUrl, PaymentStatus Status);
```

### AppDbContext (Infrastructure)

`public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)`. The members do not change.

### PaymentConfiguration (Infrastructure), Currency line only

```csharp
builder.Property(p => p.Currency)
    .HasConversion(c => c.ToString().ToUpperInvariant(), s => Enum.Parse<Currency>(s, true))
    .HasMaxLength(3)
    .IsRequired();
```

- `Currency.Eur` is stored as `"EUR"`. The `ignoreCase: true` parse reads `"EUR"` from existing rows back as `Currency.Eur`.
- Pass `true` as a positional argument. An expression tree cannot contain a named argument.
- The `Method` and `Status` conversions do not change (`"Ideal"`, `"Klarna"`, `"Pending"`).

### HTTP, no change

`POST /payments`. The request is `{ "amount": number, "currency": string, "method": string }` and enum input is case-insensitive. The response is 201 `{ "paymentId", "redirectUrl", "status": "Pending" }`, or 400 with a validation problem. `GET /health` returns 200 or 503. `StrictEnumConverter` already compares names ignoring case, so `"EUR"`, `"eur"` and `"Eur"` all read as `Currency.Eur`. The response contains no currency, so the Api layer needs no JSON mapping for `Currency`.

## 3. Tests

New test, in `tests/ApmPlayground.IntegrationTests/CreatePaymentTests.cs`:

| Name | Type | Proves |
|---|---|---|
| `CreatePayment_Currency_StoresIsoCodeInColumn(string currency, string expectedColumnValue)` with `[InlineData("EUR", "EUR")]`, `[InlineData("eur", "EUR")]`, `[InlineData("Gbp", "GBP")]`, `[InlineData("usd", "USD")]` | Integration | The raw `Currency` column holds the upper-case ISO code, not the member name, for any input case. This is the acceptance test: `"EUR"` is stored as `"EUR"`. |

- The test POSTs a valid body with `currency`. It reads the created payment id through `ReadCreatedPaymentAsync`, so cleanup still works. Then it reads the raw column with `db.Database.SqlQuery<string>($"""SELECT "Currency" AS "Value" FROM "Payments" WHERE "Id" = {body.PaymentId}""").SingleAsync()` and checks that the value equals `expectedColumnValue`.

Changed tests. These change only the names and the style. They test the same behavior:
- `CreatePaymentTests.CreatePayment_ValidRequest_PersistsPayment`: `InlineData` uses `Currency.Eur` and `Currency.Usd`. The wire input stays `"EUR"` and `"USD"`. This test proves the round trip: `"EUR"` is stored and read back as `Currency.Eur`.
- `CreatePaymentTests.CreatePayment_MethodAndCurrencyInAnyCase_Returns201`: `InlineData` uses `Currency.Eur`, `Currency.Gbp` and `Currency.Usd`. The body becomes `$$"""{"amount":{{RawAmount()}},"currency":"{{currency}}","method":"{{method}}"}"""`.
- `CreatePaymentTests.CreatePayment_PropertyNameInOtherCase_ErrorKeyIsCamelCase`: the body becomes `$$"""{"amount":{{RawAmount()}},"currency":"{{SupportedCurrency()}}","Method":"paypal"}"""`.
- `CreatePaymentTests.CreatePayment_EmptyObject_Returns400WithErrorForEachField`: the body becomes `"""{}"""`.
- `CreatePaymentTests.CreatePayment_MalformedJson_Returns400`: the body becomes `"""{ "amount": 10.50, "currency": """`. Keep the trailing space, so the content is the same.
- `CreatePaymentValidatorTests.Validate_ValidRequest_ReturnsNoErrors`: `InlineData` uses `Currency.Eur`, `Currency.Gbp` and `Currency.Usd`.
- The other tests change only by `sealed` on the class.

## 4. Edge cases

| Edge case (spec) | Test |
|---|---|
| `"EUR"`, `"eur"` and `"Eur"` are still accepted | `CreatePayment_MethodAndCurrencyInAnyCase_Returns201` (`eur`, `Eur`, `gbp`, `Usd`), `CreatePayment_ValidRequest_PersistsPayment` (`EUR`, `USD`) |
| Response still has `"status": "Pending"` | `CreatePayment_ValidRequest_Returns201WithPendingStatusAndRedirectUrl` |
| Column stores `"EUR"`, not `"Eur"` | `CreatePayment_Currency_StoresIsoCodeInColumn` (new) |
| Stored ISO code reads back as the enum member | `CreatePayment_ValidRequest_PersistsPayment`, `CreatePayment_MethodAndCurrencyInAnyCase_Returns201` |
| Unknown or combined currency is still rejected (`"JPY"`, `"GBP, USD"`, `0`, `null`) | `CreatePayment_FieldWithInvalidValue_Returns400WithInvalidValueError` |
| EF can still load a `Payment` (Id, Status and CreatedAt come from the row) | `CreatePayment_ValidRequest_PersistsPayment` (asserts `Status` and the loaded fields), `CreatePayment_*` tests that look up the row by `Id` |
| Domain constructor behavior does not change | `PaymentTests.Constructor_SetsPendingStatusAndNewId`, `PaymentTests.Constructor_TwoPayments_HaveDifferentIds` |

## 5. Pattern

None.

## 6. Decisions

1. **Currency storage mapping**: use an inline `HasConversion` with `ToString().ToUpperInvariant()` and `Enum.Parse<Currency>(s, true)` in `PaymentConfiguration`. Do not add a lookup table or a separate `ValueConverter` class (KISS). The ISO code is the member name in upper case for all three members.
2. **No Api JSON mapping for Currency**: `StrictEnumConverter` already reads case-insensitively, and no response writes a currency. A write mapping would be YAGNI.
3. **Records are sealed**: a record is a class, and the style says that classes are `sealed`. `CreatePaymentResponse` stays positional. The positional constructor already requires every value, so `required` is not needed. JSON deserialization in the tests still works.
4. **CreatePaymentResult keeps its private constructor**: a primary constructor cannot be private, and the private constructor enforces the use of `Success` and `Invalid`. IDE0290 does not flag it.
5. **Test classes and fixtures are sealed**: xUnit supports this, and nothing inherits from them.
6. **JSON fragments in tests stay escaped**: `InlineData` values such as `"\"JPY\""`, the `ValidRawFields` values `$"\"{...}\""` and the field fragment in `ToJsonObject` start or end with a `"` character. A single-line raw string cannot start or end with `"`, and a multi-line raw string in an attribute is harder to read. Only full JSON documents become raw strings. Values such as `"0"`, `"true"` and `"null"` are not quoted JSON and stay as plain strings.
7. **Program.cs `using (var scope ...)` block stays**: its scope is not the rest of the program, because `app.Run()` comes after it. IDE0063 does not apply.
8. **CreatePaymentUseCaseTests constructor stays**: the field initializer cannot reference the `_repository` field, and `.editorconfig` prefers block-bodied constructors.
9. **No migration**: the schema comes from `EnsureCreated`. The column type and length (`varchar(3)`) and the values do not change.
- **Loop 1 additions** (orchestrator, from a reviewer finding): the `/health` handler takes and passes the `CancellationToken`. The storage note moved from the Domain `Currency` comment to `PaymentConfiguration`. `HealthTests` disposes the response.
