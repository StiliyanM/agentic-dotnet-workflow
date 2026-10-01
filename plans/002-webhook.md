# Plan 002-webhook

`POST /webhooks/provider` receives `{eventId, paymentId, status}` signed with HMAC-SHA256. The API checks the signature over the raw body bytes, processes each `eventId` one time only, and changes the payment status. Out-of-order events and terminal-status rules are left to spec 003.

## 1. Files

Domain (`src/AgenticPayments.Domain/`)
- `Payments/PaymentStatus.cs`: add `Succeeded`, `Failed` (after `Pending`).
- `Payments/Payment.cs`: add `ChangeStatus(PaymentStatus status)`.
- `Webhooks/ProcessedWebhookEvent.cs` (new): the record of a processed provider event.

Application (`src/AgenticPayments.Application/`)
- `Payments/IPaymentRepository.cs`: add `FindAsync`.
- `Webhooks/ProviderPaymentStatus.cs` (new): the statuses that the provider can send.
- `Webhooks/ProviderWebhookRequest.cs` (new): request contract.
- `Webhooks/ProviderWebhookValidator.cs` (new): FluentValidation rules.
- `Webhooks/ProviderWebhookOutcome.cs` (new): outcome enum.
- `Webhooks/ProviderWebhookResult.cs` (new): use-case result.
- `Webhooks/IWebhookEventRepository.cs` (new): port for processed events.
- `Webhooks/ProcessProviderWebhookUseCase.cs` (new): the use case.
- `ApplicationServiceCollectionExtensions.cs`: register `ProcessProviderWebhookUseCase` (scoped).

Infrastructure (`src/AgenticPayments.Infrastructure/`)
- `Persistence/AppDbContext.cs`: add `DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents`.
- `Persistence/ProcessedWebhookEventConfiguration.cs` (new): key `EventId`, max length, FK to `Payment`.
- `Persistence/PaymentRepository.cs`: implement `FindAsync`.
- `Persistence/WebhookEventRepository.cs` (new): implements `IWebhookEventRepository`.
- `InfrastructureServiceCollectionExtensions.cs`: register `IWebhookEventRepository` (scoped).

Api (`src/AgenticPayments.Api/`)
- `Webhooks/ProviderWebhookOptions.cs` (new): the configured secret.
- `Webhooks/ProviderWebhookSignature.cs` (new): reads the raw body and checks the HMAC header.
- `Webhooks/WebhookEndpoints.cs` (new): maps `POST /webhooks/provider`.
- `Program.cs`: bind and validate `ProviderWebhookOptions` on start; call `app.MapWebhookEndpoints()`.
- `appsettings*.json`: no change. No secret is committed.

Tests
- `tests/AgenticPayments.IntegrationTests/Infrastructure/ApiFactory.cs`: `UseSetting("Webhooks:Provider:Secret", WebhookSecret)` with a public test-only constant `WebhookSecret`.
- `tests/AgenticPayments.UnitTests/Payments/FakePaymentRepository.cs`: implement `FindAsync` over a seedable list.
- `tests/AgenticPayments.UnitTests/Webhooks/FakeWebhookEventRepository.cs` (new).
- `tests/AgenticPayments.UnitTests/Payments/PaymentTests.cs`: add `ChangeStatus` test.
- `tests/AgenticPayments.UnitTests/Webhooks/ProviderWebhookValidatorTests.cs` (new).
- `tests/AgenticPayments.UnitTests/Webhooks/ProcessProviderWebhookUseCaseTests.cs` (new).
- `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs` (new).

## 2. Public types and signatures

### Domain
```csharp
namespace AgenticPayments.Domain.Payments;
public enum PaymentStatus { Pending, Succeeded, Failed }

public sealed class Payment(decimal amount, Currency currency, PaymentMethod method)
{
    // existing members unchanged
    public void ChangeStatus(PaymentStatus status);   // sets Status. No transition rules in 002 (spec 003 adds them here).
}

namespace AgenticPayments.Domain.Webhooks;
public sealed class ProcessedWebhookEvent(string eventId, Guid paymentId)
{
    public const int MaxEventIdLength = 200;
    public string EventId { get; private set; }            // = eventId
    public Guid PaymentId { get; private set; }            // = paymentId
    public DateTimeOffset ProcessedAt { get; private set; } // = DateTimeOffset.UtcNow
}
```

### Application
```csharp
namespace AgenticPayments.Application.Payments;
public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken);
    Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken);   // tracked entity, null if unknown
}

namespace AgenticPayments.Application.Webhooks;
public enum ProviderPaymentStatus { Succeeded, Failed }

public sealed record ProviderWebhookRequest
{
    public required string EventId { get; init; }
    public required Guid PaymentId { get; init; }
    public required ProviderPaymentStatus Status { get; init; }
}

public sealed class ProviderWebhookValidator : AbstractValidator<ProviderWebhookRequest>
{
    public ProviderWebhookValidator();
}

public enum ProviderWebhookOutcome { Processed, Duplicate, PaymentNotFound, Invalid }

public sealed class ProviderWebhookResult
{
    public ProviderWebhookOutcome Outcome { get; }
    public IDictionary<string, string[]> Errors { get; }   // empty unless Invalid
    public static ProviderWebhookResult Processed();
    public static ProviderWebhookResult Duplicate();
    public static ProviderWebhookResult PaymentNotFound();
    public static ProviderWebhookResult Invalid(IDictionary<string, string[]> errors);
}

public interface IWebhookEventRepository
{
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken);
    // Saves the event row and the changed payment in one SaveChanges (one transaction).
    // Returns false, and saves nothing, when the eventId is already stored (unique violation).
    Task<bool> TryRecordAsync(ProcessedWebhookEvent webhookEvent, Payment payment, CancellationToken cancellationToken);
}

public sealed class ProcessProviderWebhookUseCase(
    IValidator<ProviderWebhookRequest> validator,
    IPaymentRepository payments,
    IWebhookEventRepository webhookEvents)
{
    public Task<ProviderWebhookResult> ExecuteAsync(ProviderWebhookRequest request, CancellationToken cancellationToken);
}
```
Validator (`RuleLevelCascadeMode = CascadeMode.Stop`, keys via `OverridePropertyName`):

| Key | Rule | Message |
|---|---|---|
| `eventId` | `NotEmpty` (null, empty, whitespace) | `EventId is required.` |
| `eventId` | `MaximumLength(ProcessedWebhookEvent.MaxEventIdLength)` | `EventId must be at most 200 characters.` |
| `paymentId` | `NotEmpty` (`Guid.Empty`) | `PaymentId is required.` |
| `status` | `IsInEnum` | `Status has an invalid value.` |

Use case order: `ThrowIfNull(request)` -> validate (`Invalid(validationResult.ToDictionary())`) -> `ExistsAsync(eventId)` true -> `Duplicate()` -> `FindAsync(paymentId)` null -> `PaymentNotFound()` -> `payment.ChangeStatus(map(status))` -> `TryRecordAsync(new ProcessedWebhookEvent(eventId, paymentId), payment)` false -> `Duplicate()`, true -> `Processed()`. Map: private static switch `Succeeded => PaymentStatus.Succeeded`, `Failed => PaymentStatus.Failed`, `_ => throw new ArgumentOutOfRangeException(...)`.

### Infrastructure
```csharp
namespace AgenticPayments.Infrastructure.Persistence;
public sealed class ProcessedWebhookEventConfiguration : IEntityTypeConfiguration<ProcessedWebhookEvent>;
// HasKey(EventId); EventId HasMaxLength(MaxEventIdLength); HasOne<Payment>().WithMany().HasForeignKey(PaymentId) (required, cascade delete).
public sealed class WebhookEventRepository(AppDbContext db) : IWebhookEventRepository;
// TryRecordAsync: db.ProcessedWebhookEvents.Add(e); SaveChangesAsync; catch DbUpdateException whose InnerException is
// PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } -> return false. The payment is tracked by the same scoped DbContext.
```

### Api
```csharp
namespace AgenticPayments.Api.Webhooks;
public sealed class ProviderWebhookOptions
{
    public const string SectionName = "Webhooks:Provider";
    public string Secret { get; set; } = string.Empty;
}

public static class ProviderWebhookSignature
{
    public const string HeaderName = "X-Provider-Signature";
    public const string Prefix = "sha256=";
    // Enables buffering, reads the whole body, rewinds it to position 0, then checks the header.
    public static Task<bool> IsValidAsync(HttpRequest request, string secret, CancellationToken cancellationToken);
}

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app);
}
```
`Program.cs`: `builder.Services.AddOptions<ProviderWebhookOptions>().BindConfiguration(ProviderWebhookOptions.SectionName).Validate(o => !string.IsNullOrWhiteSpace(o.Secret), "Webhooks:Provider:Secret is required.").ValidateOnStart();`

### HTTP: `POST /webhooks/provider`
Headers: `Content-Type: application/json`, `X-Provider-Signature: sha256=<hex of HMACSHA256(key = UTF-8 bytes of secret, data = raw request body bytes)>`. Hex is 64 chars, any case.
Body: `{ "eventId": "evt_123", "paymentId": "<guid>", "status": "succeeded" | "failed" }` (status any case).

Check order and responses:
1. Signature (before any body parsing): header missing, more than one value, no `sha256=` prefix, not hex, not 32 bytes, or mismatch -> **401** `application/problem+json` (`TypedResults.Problem(statusCode: 401)`).
2. Body via `JsonRequestBody.ReadAsync<ProviderWebhookRequest>`: not JSON content type -> **415** no body; malformed/empty/not an object -> **400** plain problem; missing field -> **400** validation problem `"{Field} is required."`; unknown status (`"refunded"`, `"pending"`), numeric status, non-GUID paymentId -> **400** validation problem `"{Field} has an invalid value."`.
3. Use case: `Invalid` -> **400** `TypedResults.ValidationProblem(errors)`; `PaymentNotFound` -> **404** `TypedResults.Problem(statusCode: 404)`; `Processed` and `Duplicate` -> **200** `TypedResults.Ok()` with no body.

## 3. Tests

Unit (`AgenticPayments.UnitTests`)
- `PaymentTests.ChangeStatus_SetsStatus` (Theory: `Succeeded`, `Failed`): status changes.
- `ProviderWebhookValidatorTests`:
  - `Validate_ValidRequest_ReturnsNoErrors` (eventId of 1 and of 200 chars, both statuses).
  - `Validate_EventIdEmpty_ReturnsEventIdError` (Theory: `""`, `"  "`, null).
  - `Validate_EventIdTooLong_ReturnsEventIdError` (201 chars).
  - `Validate_PaymentIdEmpty_ReturnsPaymentIdError`.
  - `Validate_StatusUndefined_ReturnsStatusError` (`(ProviderPaymentStatus)99`).
- `ProcessProviderWebhookUseCaseTests` (fakes; real validator):
  - `ExecuteAsync_NewEvent_ChangesPaymentStatusAndRecordsEvent` (Theory: `Succeeded -> PaymentStatus.Succeeded`, `Failed -> PaymentStatus.Failed`): outcome `Processed`, one recorded event with the eventId and paymentId.
  - `ExecuteAsync_KnownEventId_ReturnsDuplicateAndChangesNothing`: `ExistsAsync` true; status stays `Pending`; nothing recorded.
  - `ExecuteAsync_RecordRejected_ReturnsDuplicate`: `TryRecordAsync` returns false (concurrent duplicate path).
  - `ExecuteAsync_UnknownPayment_ReturnsPaymentNotFoundAndRecordsNothing`.
  - `ExecuteAsync_InvalidRequest_ReturnsInvalidWithErrorsAndRecordsNothing`.

Integration (`ProviderWebhookTests`, uses `ApiFactory`; helper signs a raw string body with `ApiFactory.WebhookSecret`; payments seeded through `POST /payments` or `AppDbContext`; clean up created rows)
- `Webhook_ValidSignedEvent_Returns200AndUpdatesStatus` (Theory: `"succeeded"`, `"FAILED"`): 200, DB status changed, one event row.
- `Webhook_MissingSignature_Returns401AndChangesNothing`.
- `Webhook_InvalidSignature_Returns401` (Theory: signed with another secret; body changed after signing; no prefix; non-hex; wrong length).
- `Webhook_UppercaseHexSignature_Returns200`.
- `Webhook_UnsignedMalformedBody_Returns401`: proves signature check runs before body parsing.
- `Webhook_SignedMalformedJson_Returns400Problem`: no `errors` member.
- `Webhook_SignedNonJsonContentType_Returns415`.
- `Webhook_MissingField_Returns400WithRequiredError` (Theory: `eventId`, `paymentId`, `status`).
- `Webhook_InvalidStatus_Returns400WithStatusError` (Theory: `"refunded"`, `"pending"`, `1`).
- `Webhook_EmptyEventId_Returns400WithEventIdError`: validator through HTTP.
- `Webhook_UnknownPayment_Returns404AndStoresNoEvent`.
- `Webhook_DuplicateEventId_Returns200AndDoesNotChangeStatusAgain`: first `succeeded`, then same eventId with `failed`; status stays `Succeeded`; one event row.
- `Webhook_ConcurrentDuplicates_ProcessOnce`: 5 parallel identical requests; all 200; one event row.
- `TryRecordAsync_EventIdAlreadyStored_ReturnsFalseAndSavesNothing`: event row inserted through `AppDbContext`; in a new scope load the payment with `IPaymentRepository.FindAsync`, `ChangeStatus(Failed)`, call `IWebhookEventRepository.TryRecordAsync` -> false; a fresh scope shows the old status. Proves the unique-key path and the single transaction deterministically.
- `Startup_WebhookSecretMissing_Fails`: `WithWebHostBuilder(UseSetting(secret, ""))`, `CreateClient()` throws `OptionsValidationException`.

## 4. Edge cases

| Edge case | Test |
|---|---|
| Missing / malformed / wrong signature | `Webhook_MissingSignature_...`, `Webhook_InvalidSignature_...` |
| Body changed after signing (raw bytes are signed) | `Webhook_InvalidSignature_...` (changed body row) |
| Hex case | `Webhook_UppercaseHexSignature_Returns200` |
| Signature before body parsing | `Webhook_UnsignedMalformedBody_Returns401` |
| Malformed JSON, wrong content type | `Webhook_SignedMalformedJson_...`, `Webhook_SignedNonJsonContentType_...` |
| Missing field | `Webhook_MissingField_...` |
| Unknown, `pending`, numeric status | `Webhook_InvalidStatus_...`, `Validate_StatusUndefined_...` |
| Empty / too long eventId, empty paymentId | validator tests, `Webhook_EmptyEventId_...` |
| Unknown paymentId | `ExecuteAsync_UnknownPayment_...`, `Webhook_UnknownPayment_...` |
| Repeated eventId | `ExecuteAsync_KnownEventId_...`, `Webhook_DuplicateEventId_...` |
| Concurrent duplicates (unique violation) | `ExecuteAsync_RecordRejected_...`, `TryRecordAsync_EventIdAlreadyStored_...`, `Webhook_ConcurrentDuplicates_...` |
| Event and status saved atomically | `TryRecordAsync_EventIdAlreadyStored_...` |
| Secret not configured | `Startup_WebhookSecretMissing_Fails` |

## 5. Pattern

None.

## 6. Decisions

- **D1 Header and format.** Header `X-Provider-Signature`, value `sha256=` + hex of HMAC-SHA256. Key: UTF-8 bytes of the configured secret. Data: the exact raw request body bytes (no parsing, no normalization). Hex is decoded with `Convert.FromHexString` (any case). The decoded bytes are compared with `CryptographicOperations.FixedTimeEquals`. A header with more than one value is invalid.
- **D2 Where the signature lives.** It is an HTTP concern, so `ProviderWebhookSignature` is in Api. The endpoint calls it first, then `JsonRequestBody.ReadAsync`. `EnableBuffering` plus a rewind lets the same body be read twice. The secret goes in as a `string` from `IOptions<ProviderWebhookOptions>`. Because unit tests cannot reference Api, the signature is proven by integration tests only.
- **D3 Responses.** 401 problem for any signature failure (no detail, to give no hints). 415/400 as in the existing `JsonRequestBody` contract. 404 problem for an unknown payment. 200 with no body for success and for a duplicate, so that provider retries stop and a duplicate gives the same result as the first delivery.
- **D4 Status values.** The webhook accepts only `succeeded` and `failed` (`ProviderPaymentStatus`, any case through `StrictEnumConverter`). `pending` is rejected with 400. The provider vocabulary is a separate enum from `PaymentStatus`, and the use case maps it. `PaymentStatus` gets `Succeeded` and `Failed` now, because 002 needs a status to change to. The column already stores the enum name as text, so the new values need no schema change.
- **D5 Left to 003.** No transition rules: in 002 any processed event sets the status, including `Failed -> Succeeded`. No event ordering. 003 adds its rules inside `Payment.ChangeStatus` (which can then return or throw a rejection) and can add fields such as a provider timestamp to `ProcessedWebhookEvent`. The duplicate check runs before the payment is loaded, so 003 rules do not affect duplicate answers. Concurrent events with different eventIds for the same payment: last write wins in 002.
- **D6 Idempotency.** Table `ProcessedWebhookEvents` with primary key `EventId` (varchar(200)), `PaymentId` (FK to `Payments`, cascade delete so existing test cleanup still works), `ProcessedAt`. `ExistsAsync` answers the normal repeat. A concurrent repeat that passes `ExistsAsync` fails on the primary key (`23505`); `TryRecordAsync` returns false and the use case returns `Duplicate` (200).
- **D7 One transaction.** The event row and the payment status are saved by one `SaveChangesAsync` on the scoped `AppDbContext`, which EF Core runs in one transaction. If the event insert fails, the status change is not saved. No explicit transaction and no Unit of Work abstraction.
- **D8 Duplicate check first.** Order is validate -> duplicate -> payment lookup. A repeated eventId never touches the payment.
- **D9 Secret source.** Config key `Webhooks:Provider:Secret`. No value in any committed file. Development: environment variable `Webhooks__Provider__Secret` (or any other configuration source outside the repo). Tests: `ApiFactory.UseSetting` with a test-only constant. The app fails at start (`ValidateOnStart`) when the secret is empty, so a missing secret is never a silent open endpoint.
- **D10 Schema.** Keep `EnsureCreated`. The integration tests use a fresh container, so the new table is created. A local development database that already has the `Payments` table must be dropped once (`EnsureCreated` does nothing when tables exist). There is no data to keep yet, and migrations would add a design package, generated files and a startup change that no spec needs yet. Switch to migrations when a spec needs to keep data across schema changes.
- **D11 eventId.** A string (provider IDs are not GUIDs), 1 to 200 characters, compared exactly (case-sensitive).
