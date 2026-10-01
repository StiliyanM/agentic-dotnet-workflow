# Plan 004-layered-structure

This is a refactor. `POST /payments` and `GET /health` behave the same as before: same routes, status codes, bodies, error keys and messages.

## 1. Files

New projects. Create each with `dotnet new classlib -f net10.0 -o src/<Name> -n <Name>`, delete `Class1.cs`, and run `dotnet sln ApmPlayground.slnx add src/<Name>/<Name>.csproj`.

| Project | Project references | Packages (`dotnet add <project> package <name>`) |
|---|---|---|
| `src/ApmPlayground.Domain` | none | none |
| `src/ApmPlayground.Application` | Domain | `FluentValidation`, `FluentValidation.DependencyInjectionExtensions` |
| `src/ApmPlayground.Infrastructure` | Application, Domain | `Npgsql.EntityFrameworkCore.PostgreSQL` (use version 10.0.3, the same as now) |

Domain (`src/ApmPlayground.Domain/Payments/`)
- `Payment.cs`: move from Api. Same content. New namespace.
- `PaymentMethod.cs`: move from Api. New namespace.
- `PaymentStatus.cs`: move from Api. New namespace.
- `SupportedCurrencies.cs`: move from Api. New namespace.

Application (`src/ApmPlayground.Application/`)
- `Payments/CreatePaymentRequest.cs`: move from Api. Same record shape. New namespace.
- `Payments/CreatePaymentResponse.cs`: move from Api. Same record shape. New namespace.
- `Payments/PaymentMethodNames.cs`: move from Api. Same content. New namespace.
- `Payments/CreatePaymentValidator.cs`: new FluentValidation validator. It replaces the static Api validator.
- `Payments/IPaymentRepository.cs`: new port for saving a payment.
- `Payments/CreatePaymentResult.cs`: new result type of the use case.
- `Payments/CreatePaymentUseCase.cs`: new use case. It holds the logic that is in the endpoint now (validate, create, save, build the redirect URL).
- `ApplicationServiceCollectionExtensions.cs`: `AddApplication()`.

Infrastructure (`src/ApmPlayground.Infrastructure/`)
- `Persistence/AppDbContext.cs`: move from `Api/Data`. Keep `DbSet<Payment> Payments`. `OnModelCreating` calls `ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly)`.
- `Persistence/PaymentConfiguration.cs`: new `IEntityTypeConfiguration<Payment>`. It holds the same mapping that is in `OnModelCreating` now (precision, max length 3 and required, string conversions).
- `Persistence/PaymentRepository.cs`: new. Implements `IPaymentRepository` with `AppDbContext` (Add + SaveChangesAsync).
- `InfrastructureServiceCollectionExtensions.cs`: `AddInfrastructure(IConfiguration)`.

Api
- `ApmPlayground.Api.csproj`: remove the `Npgsql.EntityFrameworkCore.PostgreSQL` package (it comes through Infrastructure). Add project references to Application and Infrastructure.
- `Program.cs`: replace `AddDbContext` with `AddApplication()` and `AddInfrastructure(builder.Configuration)`. Keep `EnsureCreated`, the `/health` endpoint and `MapPaymentEndpoints()`. Change the usings.
- `Payments/PaymentEndpoints.cs`: the handler binds `CreatePaymentRequest`, calls `CreatePaymentUseCase.ExecuteAsync` and maps the result: `TypedResults.ValidationProblem(result.Errors)` or `TypedResults.Json(result.Response, statusCode: 201)`. Remove the `RedirectBaseUrl` constant from this file.
- Delete `Data/AppDbContext.cs`, `Payments/CreatePaymentRequest.cs`, `Payments/CreatePaymentResponse.cs`, `Payments/CreatePaymentValidator.cs`, `Payments/Payment.cs`, `Payments/PaymentMethod.cs`, `Payments/PaymentMethodNames.cs`, `Payments/PaymentStatus.cs`, `Payments/SupportedCurrencies.cs`.

Tests
- `tests/ApmPlayground.UnitTests/ApmPlayground.UnitTests.csproj`: replace the Api project reference with references to Domain and Application.
- `tests/ApmPlayground.UnitTests/Payments/PaymentTests.cs`: change only the `using` to `ApmPlayground.Domain.Payments`.
- `tests/ApmPlayground.UnitTests/Payments/CreatePaymentValidatorTests.cs`: change the usings and the way the tests call and assert the validator (see section 3). The test names, the data and the messages stay the same.
- `tests/ApmPlayground.UnitTests/Payments/CreatePaymentUseCaseTests.cs`: new use case tests.
- `tests/ApmPlayground.UnitTests/Payments/FakePaymentRepository.cs`: new fake for `IPaymentRepository`.
- `tests/ApmPlayground.IntegrationTests/CreatePaymentTests.cs`: change only the `using` lines (see Decisions D1). No other change.
- `tests/ApmPlayground.IntegrationTests/ApmPlayground.IntegrationTests.csproj`: no change. The project reference to Api brings Application, Infrastructure and Domain transitively.

## 2. Public types and signatures

### Domain, namespace `ApmPlayground.Domain.Payments`
These types do not change. Only the namespace changes.
```csharp
public class Payment
{
    public const int AmountPrecision = 18;
    public const int AmountScale = 2;
    public const decimal MaxAmount = 9999999999999999.99m;
    public Payment(decimal amount, string currency, PaymentMethod method); // Id = Guid.CreateVersion7(), Status = Pending, CreatedAt = UtcNow
    public Guid Id { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
public enum PaymentMethod { Ideal, Klarna }
public enum PaymentStatus { Pending }
public static class SupportedCurrencies
{
    public static IReadOnlySet<string> All { get; }            // "EUR", "GBP", "USD", ordinal
    public static bool IsSupported(string? code);
}
```

### Application, namespace `ApmPlayground.Application.Payments`
```csharp
public record CreatePaymentRequest(decimal? Amount, string? Currency, string? Method);          // unchanged
public record CreatePaymentResponse(Guid PaymentId, string RedirectUrl, string Status);        // unchanged

public static class PaymentMethodNames                                                         // unchanged
{
    public static bool TryParse(string? value, out PaymentMethod method);
    public static string ToName(PaymentMethod method);
}

public sealed class CreatePaymentValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentValidator();
}

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken);  // adds and saves
}

public sealed class CreatePaymentResult
{
    public CreatePaymentResponse? Response { get; }
    public IDictionary<string, string[]> Errors { get; }               // empty on success
    [MemberNotNullWhen(true, nameof(Response))]
    public bool IsSuccess { get; }                                     // Response is not null
    public static CreatePaymentResult Success(CreatePaymentResponse response);
    public static CreatePaymentResult Invalid(IDictionary<string, string[]> errors);
}

public sealed class CreatePaymentUseCase(IValidator<CreatePaymentRequest> validator, IPaymentRepository repository)
{
    public Task<CreatePaymentResult> ExecuteAsync(CreatePaymentRequest request, CancellationToken cancellationToken);
}
```
Validator rules. Set `RuleLevelCascadeMode = CascadeMode.Stop` in the constructor, so that each field gives at most one error. Each rule uses `OverridePropertyName` with the camelCase key.
- `Amount`, key `amount`, in this order: `NotNull` "Amount is required."; `GreaterThan(0)` "Amount must be greater than 0."; `LessThanOrEqualTo(Payment.MaxAmount)` "Amount is too large."; `Must(decimal.Round(a, Payment.AmountScale) == a)` "Amount must have at most 2 decimal places.".
- `Currency`, key `currency`: `NotEmpty` "Currency is required."; `Must(SupportedCurrencies.IsSupported)` with the message `$"Currency '{request.Currency}' is not supported."`.
- `Method`, key `method`: `NotEmpty` "Method is required."; `Must(m => PaymentMethodNames.TryParse(m, out _))` "Method must be 'ideal' or 'klarna'.".

`CreatePaymentUseCase.ExecuteAsync`: `ArgumentNullException.ThrowIfNull(request)`. Then `validator.ValidateAsync`. If the request is not valid, or `PaymentMethodNames.TryParse` fails, return `Invalid(validationResult.ToDictionary())`. Otherwise create a `Payment`, call `repository.AddAsync`, and return `Success(new CreatePaymentResponse(payment.Id, $"https://pay.example.com/{PaymentMethodNames.ToName(method)}/{payment.Id:D}", payment.Status.ToString()))`. The constant `RedirectBaseUrl = "https://pay.example.com"` moves into this class as a private constant.

Namespace `ApmPlayground.Application`:
```csharp
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services);
    // AddValidatorsFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly); AddScoped<CreatePaymentUseCase>()
}
```

### Infrastructure, namespace `ApmPlayground.Infrastructure.Persistence`
```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments { get; }   // => Set<Payment>()
    protected override void OnModelCreating(ModelBuilder modelBuilder);
}
public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder);
}
public sealed class PaymentRepository(AppDbContext db) : IPaymentRepository
{
    public Task AddAsync(Payment payment, CancellationToken cancellationToken);
}
```
Namespace `ApmPlayground.Infrastructure`:
```csharp
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration);
    // AddDbContext<AppDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Postgres")))
    //   -- read the connection string inside the lambda, not at registration (see D5)
    // AddScoped<IPaymentRepository, PaymentRepository>()
}
```

### HTTP (no change)
- `POST /payments`. Request JSON `{ "amount": decimal, "currency": string, "method": string }`.
  - 201 `application/json` `{ "paymentId": guid, "redirectUrl": "https://pay.example.com/{method}/{id}", "status": "Pending" }`, with no Location header.
  - 400 `application/problem+json` validation problem. `errors` keys are `amount`, `currency`, `method`, with one message each.
  - 400 for malformed JSON (framework binding, as now).
- `GET /health`: 200, or 503 when the database cannot be reached.

## 3. Tests

Unit tests (`ApmPlayground.UnitTests`, which references Domain and Application only)
- `PaymentTests` (both existing tests): no change except the `using`. They prove that a new payment is Pending and gets a new id.
- `CreatePaymentValidatorTests`: the same test names, InlineData and messages. Changes:
  - a field `private readonly CreatePaymentValidator _validator = new();`
  - each test calls `var result = _validator.Validate(request);` and gets a `FluentValidation.Results.ValidationResult`.
  - `Validate_ValidRequest_ReturnsNoErrors`: `Assert.True(result.IsValid)` and `Assert.Empty(result.Errors)`.
  - `AssertSingleError(ValidationResult result, string key, string message)`: `var error = Assert.Single(result.Errors);` then assert `error.PropertyName == key` and `error.ErrorMessage == message`.
  - `Validate_AllFieldsInvalid_ReturnsErrorForEachField`: `var errors = result.ToDictionary();` then assert 3 keys (`amount`, `currency`, `method`), each with a single expected message.
  - These tests prove the same rules and messages as before, and that the keys are camelCase.
- `CreatePaymentUseCaseTests` (new). Build the use case with `new CreatePaymentUseCase(new CreatePaymentValidator(), fakeRepository)`.
  - `ExecuteAsync_ValidRequest_ReturnsPendingResponseWithRedirectUrl` (Theory: `ideal`, `klarna`): the result is a success, Status is "Pending", and RedirectUrl is `https://pay.example.com/{method}/{PaymentId:D}`.
  - `ExecuteAsync_ValidRequest_AddsPaymentToRepository`: the fake has one payment with the request amount, currency, parsed method and Pending status, and its Id equals `Response.PaymentId`.
  - `ExecuteAsync_InvalidRequest_ReturnsErrorsAndDoesNotAddPayment`: an invalid amount gives a result that is not a success, with `Errors["amount"]` = ["Amount must be greater than 0."], and the fake is empty.
- `FakePaymentRepository : IPaymentRepository` with `public List<Payment> Added { get; } = [];`. `AddAsync` adds to the list and returns `Task.CompletedTask`.

Integration tests (`ApmPlayground.IntegrationTests`): no new tests. All existing `CreatePaymentTests` and `HealthTests` must pass and prove that the HTTP behavior did not change.

## 4. Edge cases
| Edge case | Covered by |
|---|---|
| HTTP behavior unchanged (201 body, no Location, 400 problem shape) | all existing integration tests |
| Only one error per field (cascade stop), for example a null amount does not also give "greater than 0" | `Validate_AmountMissing_ReturnsAmountError`, `Validate_CurrencyMissing_...`, `Validate_MethodMissing_...` (the `Assert.Single` on `result.Errors`) |
| Error keys stay camelCase in HTTP | `AssertSingleError` checks `PropertyName`; integration `AssertValidationProblemAsync` checks `errors.amount/currency/method` |
| Currency and method are case-sensitive (`eur`, `IDEAL`, `Klarna` are rejected) | `Validate_UnsupportedCurrency_...`, `Validate_UnknownMethod_...` |
| Amount at the maximum is valid, above it is rejected | `Validate_ValidRequest_ReturnsNoErrors` (9999999999999999.99), `Validate_AmountAboveMaximum_...` |
| More than 2 decimals | `Validate_AmountWithMoreThanTwoDecimals_...` |
| Empty string vs null for currency and method | `Validate_CurrencyMissing_...`, `Validate_MethodMissing_...` |
| Invalid request is not saved | `ExecuteAsync_InvalidRequest_ReturnsErrorsAndDoesNotAddPayment` |
| Test connection string from `ApiFactory.UseSetting` is used | all integration tests (D5) |
| Malformed JSON still gives 400 | `CreatePayment_MalformedJson_Returns400` |

## 5. Pattern
None. The layers are an architecture rule, not a pattern from this plan. `IPaymentRepository` is the port that `docs/architecture.md` requires, not a generic repository.

## 6. Decisions
- **D1: Integration test file.** `AppDbContext` moves to `ApmPlayground.Infrastructure.Persistence`, and `PaymentMethod`/`PaymentStatus`/`CreatePaymentResponse` move to Domain and Application. The `using ApmPlayground.Api.Data;` line cannot compile once that namespace is gone, so the file must change. In `CreatePaymentTests.cs`, change ONLY the using lines: replace `using ApmPlayground.Api.Data;` and `using ApmPlayground.Api.Payments;` with `using ApmPlayground.Application.Payments;`, `using ApmPlayground.Domain.Payments;` and `using ApmPlayground.Infrastructure.Persistence;`. Do not change any test name, test body, helper or assertion. Do not change the csproj. The alternative was to keep the old `ApmPlayground.Api.*` namespaces inside the new projects. That alternative was rejected: a Domain or Infrastructure type with an `Api` namespace breaks the layer rules, and `docs/architecture.md` says that integration tests use `AppDbContext` "from Infrastructure". The acceptance "integration tests do not change" is read as: their tests and behavior do not change, and only the imports follow the moved types.
- **D2: Validator name.** Keep the class name `CreatePaymentValidator` (now in `ApmPlayground.Application.Payments`, and no longer static), so that the unit test class and names stay the same.
- **D3: camelCase keys.** Use `OverridePropertyName("amount" | "currency" | "method")` on each rule. Do not use the global `ValidatorOptions.Global.PropertyNameResolver`, because it is static global state. `ValidationResult.ToDictionary()` then gives the camelCase keys directly to `TypedResults.ValidationProblem`.
- **D4: Where validation runs.** It runs in the use case, through the injected `IValidator<CreatePaymentRequest>`. The endpoint only maps the `CreatePaymentResult` to HTTP. This keeps the endpoint thin. No exceptions are used for validation.
- **D5: Connection string.** `AddInfrastructure` reads `configuration.GetConnectionString("Postgres")` inside the `AddDbContext` options lambda, not when it registers. `Program` passes `builder.Configuration`, which is a live `ConfigurationManager`. The `WebApplicationFactory` override (`UseSetting`) is therefore seen when the DbContext is resolved, as it is today with the `sp`-based lambda.
- **D6: SupportedCurrencies goes to Domain** (the supported currency set is a domain rule). **PaymentMethodNames goes to Application**: it maps the wire names to the enum, and the validator and the use case use it. Domain stays free of contract strings.
- **D7: Redirect URL** construction and its base URL constant move from the endpoint to `CreatePaymentUseCase`. The HTTP output is the same.
- **D8: Port shape.** `IPaymentRepository.AddAsync` both adds and saves (one call, one write). Do not add a Unit of Work.
- **D9: Analyzer safety.** `TreatWarningsAsErrors` and latest-recommended are on. New concrete classes are `public sealed`, to avoid CA1812 on DI- or reflection-created internal types. The DI extension classes are named `*ServiceCollectionExtensions`, not `DependencyInjection`, to avoid CA1724. Public methods call `ArgumentNullException.ThrowIfNull` on reference parameters (CA1062), as the code does now.
- **D10: EnsureCreated and /health** stay in `Program.cs`. They use `AppDbContext` from Infrastructure, which Api may reference. Migrations are out of scope.
- **D11: Packages.** No extra packages are needed beyond those in the table. `IConfiguration`/`GetConnectionString` and `IServiceCollection` come transitively through EF Core Relational and FluentValidation.DependencyInjectionExtensions. If the build reports a missing `GetConnectionString`, add `Microsoft.Extensions.Configuration.Abstractions` to Infrastructure.
- **D12: Contracts** (`CreatePaymentRequest` with nullable fields and string method/currency, and `CreatePaymentResponse` with string status) do not change. Spec 005 changes them.
- **D13: CancellationToken.** The endpoint handler takes a `CancellationToken` and passes it to the use case and to `SaveChangesAsync`. The behavior does not change.
- **D14: Required check for strings** (added by the orchestrator in loop 1, from a reviewer finding). Use `Must(v => !string.IsNullOrEmpty(v))`, not `NotEmpty()`. `NotEmpty()` treats a whitespace-only value as missing, which changes the message from spec 001.
