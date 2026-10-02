# Plan 003-out-of-order

A terminal status (`Succeeded`, `Failed`) is final. An event for a terminal payment is ignored, recorded (D4) and answered with 200 and no body (D6). A repeated `eventId` with a different payload gives 200, changes nothing and logs a warning (D5). Two events for one `Pending` payment that run at the same time give the same result as two events that run one after the other (C1). The user decisions D1–D6 in the spec are applied as written. The contract from spec 002 does not change.

## 1. Files

Domain (`src/AgenticPayments.Domain/`)
- `Payments/Payment.cs`: replace `ChangeStatus` with `TryChangeStatus` (terminal status is final).
- `Webhooks/ProcessedWebhookEvent.cs`: add `PayloadHash` (nullable, for rows from before this spec) and a nullable constructor parameter for it (`string? payloadHash`, no null guard).

Application (`src/AgenticPayments.Application/`)
- `Webhooks/WebhookPayloadHash.cs` (new): computes the stored payload hash.
- `Webhooks/WebhookRecordResult.cs` (new): result enum of `IWebhookEventRepository.RecordAsync`.
- `Webhooks/IWebhookEventRepository.cs`: replace `ExistsAsync` with `FindAsync`; replace `TryRecordAsync` with `RecordAsync`.
- `Webhooks/ProviderWebhookOutcome.cs`: add `Ignored`, `DuplicatePayloadMismatch`.
- `Webhooks/ProviderWebhookResult.cs`: add factories `Ignored()`, `DuplicatePayloadMismatch()`.
- `Webhooks/ProcessProviderWebhookUseCase.cs`: transition rule, hash compare, one re-evaluation after a concurrency conflict.

Infrastructure (`src/AgenticPayments.Infrastructure/`)
- `Persistence/PaymentConfiguration.cs`: shadow concurrency token mapped to PostgreSQL `xmin`.
- `Persistence/ProcessedWebhookEventConfiguration.cs`: `PayloadHash` max length 64, nullable.
- `Persistence/WebhookEventRepository.cs`: implement `FindAsync` and `RecordAsync`.
- `Persistence/DatabaseInitializer.cs` (new): `EnsureCreated` plus the idempotent column upgrade.

Api (`src/AgenticPayments.Api/`)
- `Webhooks/WebhookEndpoints.cs`: map `Ignored` and `DuplicatePayloadMismatch` to 200; log the warning for a mismatch.
- `Program.cs`: call `DatabaseInitializer.InitializeAsync` instead of `EnsureCreated`.

Tests
- `tests/AgenticPayments.UnitTests/Payments/PaymentTests.cs`: replace the `ChangeStatus` test.
- `tests/AgenticPayments.UnitTests/Webhooks/WebhookPayloadHashTests.cs` (new).
- `tests/AgenticPayments.UnitTests/Webhooks/FakeWebhookEventRepository.cs`: new port members; seedable stored events (with or without hash); a configurable sequence of `RecordAsync` results; a hook that runs before a result is returned (to change the payment between attempts).
- `tests/AgenticPayments.UnitTests/Webhooks/ProcessProviderWebhookUseCaseTests.cs`: update and add tests.
- `tests/AgenticPayments.IntegrationTests/Infrastructure/CapturingLoggerProvider.cs` (new): an `ILoggerProvider` that keeps the log entries (category, level, message), added through `WithWebHostBuilder(... ConfigureLogging ...)`.
- `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs`: update the tests for the new port and the new constructor, and add tests.
- `tests/AgenticPayments.IntegrationTests/DatabaseInitializerTests.cs` (new).

## 2. Public types and signatures

### Domain
```csharp
namespace AgenticPayments.Domain.Payments;
public sealed class Payment(decimal amount, Currency currency, PaymentMethod method)
{
    // existing members unchanged; ChangeStatus is removed
    // Returns false and changes nothing when Status is already terminal (Succeeded or Failed).
    // Otherwise sets Status and returns true.
    public bool TryChangeStatus(PaymentStatus status);
}

namespace AgenticPayments.Domain.Webhooks;
// payloadHash is nullable and has no null guard (no ThrowIfNull): null means "stored before spec 003".
// Production code always passes WebhookPayloadHash.Compute(...); tests pass null to build a pre-spec-003 row.
public sealed class ProcessedWebhookEvent(string eventId, Guid paymentId, string? payloadHash)
{
    public const int MaxEventIdLength = 200;
    public const int PayloadHashLength = 64;
    public string EventId { get; private set; }            // = eventId
    public Guid PaymentId { get; private set; }            // = paymentId
    public string? PayloadHash { get; private set; }       // = payloadHash; null only for rows stored before spec 003
    public DateTimeOffset ProcessedAt { get; private set; } // = DateTimeOffset.UtcNow
}
```

### Application
```csharp
namespace AgenticPayments.Application.Webhooks;

public static class WebhookPayloadHash
{
    // Lower-case hex of SHA-256 over the UTF-8 bytes of $"{paymentId:D}|{status}",
    // where status is the ProviderPaymentStatus member name (for example "Succeeded"). Always 64 characters.
    public static string Compute(Guid paymentId, ProviderPaymentStatus status);
}

public enum WebhookRecordResult { Recorded, DuplicateEvent, PaymentChanged }

public interface IWebhookEventRepository
{
    // The stored event with this id (not tracked), or null.
    Task<ProcessedWebhookEvent?> FindAsync(string eventId, CancellationToken cancellationToken);

    // Saves the event and the current state of the payment in one SaveChanges (one transaction).
    // The payment must be tracked by the same scoped context (loaded by IPaymentRepository.FindAsync in this scope);
    // otherwise InvalidOperationException.
    // Recorded: both saved. DuplicateEvent: the eventId is already stored; nothing saved.
    // PaymentChanged: the payment row changed after it was loaded; nothing saved.
    // On DuplicateEvent and PaymentChanged the context forgets all tracked entities, so the caller must load the payment again.
    Task<WebhookRecordResult> RecordAsync(ProcessedWebhookEvent webhookEvent, Payment payment, CancellationToken cancellationToken);
}

public enum ProviderWebhookOutcome { Processed, Ignored, Duplicate, DuplicatePayloadMismatch, PaymentNotFound, Invalid }

public sealed class ProviderWebhookResult
{
    public ProviderWebhookOutcome Outcome { get; }
    public IDictionary<string, string[]> Errors { get; }   // empty unless Invalid
    public static ProviderWebhookResult Processed();
    public static ProviderWebhookResult Ignored();
    public static ProviderWebhookResult Duplicate();
    public static ProviderWebhookResult DuplicatePayloadMismatch();
    public static ProviderWebhookResult PaymentNotFound();
    public static ProviderWebhookResult Invalid(IDictionary<string, string[]> errors);
}

public sealed class ProcessProviderWebhookUseCase(
    IValidator<ProviderWebhookRequest> validator,
    IPaymentRepository payments,
    IWebhookEventRepository webhookEvents)
{
    public const int MaxAttempts = 2;
    public Task<ProviderWebhookResult> ExecuteAsync(ProviderWebhookRequest request, CancellationToken cancellationToken);
}
```
`ProviderWebhookRequest`, `ProviderPaymentStatus` (`Succeeded`, `Failed`; D1), `ProviderWebhookValidator`, `IPaymentRepository`: no change.

Use case order:
1. `ThrowIfNull(request)`; validate -> `Invalid(errors)`.
2. `hash = WebhookPayloadHash.Compute(request.PaymentId, request.Status)`.
3. Up to `MaxAttempts` times:
   1. `webhookEvents.FindAsync(eventId)` not null -> stored `PayloadHash` is null or equals `hash` -> `Duplicate()`; otherwise `DuplicatePayloadMismatch()`.
   2. `payments.FindAsync(paymentId)` null -> `PaymentNotFound()`.
   3. `applied = payment.TryChangeStatus(map(request.Status))`.
   4. `webhookEvents.RecordAsync(new ProcessedWebhookEvent(eventId, paymentId, hash), payment)`: `Recorded` -> `applied ? Processed() : Ignored()`; `DuplicateEvent` -> `Duplicate()`; `PaymentChanged` -> next attempt.
4. After `MaxAttempts` -> throw `InvalidOperationException`. Two attempts are enough: the only change is `Pending` -> terminal, and a terminal payment is never saved again, so the second attempt cannot get `PaymentChanged`.

### Infrastructure
```csharp
namespace AgenticPayments.Infrastructure.Persistence;
public sealed class WebhookEventRepository(AppDbContext db) : IWebhookEventRepository;

public static class DatabaseInitializer
{
    // EnsureCreated, then: ALTER TABLE "ProcessedWebhookEvents" ADD COLUMN IF NOT EXISTS "PayloadHash" character varying(64) NULL.
    // Safe to run on every start, on a new or an existing database.
    public static Task InitializeAsync(AppDbContext db, CancellationToken cancellationToken);
}
```
- `PaymentConfiguration`: `builder.Property<uint>("Version").IsRowVersion()` (Npgsql maps it to the system column `xmin`; no column is created).
- `ProcessedWebhookEventConfiguration`: `builder.Property(e => e.PayloadHash).HasMaxLength(ProcessedWebhookEvent.PayloadHashLength)` (nullable).
- `RecordAsync`: guard that `db.Entry(payment).State` is not `Detached`; add the event; `SaveChangesAsync`. `DbUpdateConcurrencyException` -> `ChangeTracker.Clear()`, `PaymentChanged`. `DbUpdateException` with `PostgresException { SqlState: UniqueViolation }` -> `ChangeTracker.Clear()`, `DuplicateEvent`. Catch the concurrency exception first (it derives from `DbUpdateException`).

### Api
`WebhookEndpoints.MapWebhookEndpoints` signature unchanged. The handler gets an extra `ILoggerFactory loggerFactory` parameter.

### HTTP: `POST /webhooks/provider` (unchanged contract, new outcomes)
Signature, body, 401/415/400/404 exactly as in spec 002. Outcome mapping:

| Outcome | Response |
|---|---|
| `Processed`, `Ignored`, `Duplicate` | 200, no body |
| `DuplicatePayloadMismatch` | 200, no body; log `Warning`, category `AgenticPayments.Api.Webhooks.WebhookEndpoints`, template `Webhook event {EventId} was received again with a different payload. It was not processed again.` |
| `PaymentNotFound` | 404 problem |
| `Invalid` | 400 validation problem |
| `InvalidOperationException` from the use case (attempts used up) | 500 problem (existing exception handler) |

## 3. Tests

Unit (`AgenticPayments.UnitTests`)
- `PaymentTests.TryChangeStatus_FromPending_SetsStatusAndReturnsTrue` (Theory: `Succeeded`, `Failed`).
- `PaymentTests.TryChangeStatus_FromTerminal_ReturnsFalseAndKeepsStatus` (Theory: current `Succeeded`/`Failed` x target `Pending`/`Succeeded`/`Failed`): a terminal status is final; a non-terminal status cannot replace it (A5 rule).
- `WebhookPayloadHashTests.Compute_KnownPayload_ReturnsLowercaseSha256OfCanonicalString`: equals `Convert.ToHexStringLower(SHA256.HashData(UTF8($"{id:D}|Succeeded")))`, length 64.
- `WebhookPayloadHashTests.Compute_DifferentPaymentIdOrStatus_ReturnsDifferentHash` (Theory).
- `ProcessProviderWebhookUseCaseTests`:
  - `ExecuteAsync_PendingPayment_AppliesStatusAndRecordsEventWithHash` (Theory: both statuses) (A1, A2): `Processed`, one record with eventId, paymentId, `PayloadHash == Compute(...)`.
  - `ExecuteAsync_TerminalPayment_ReturnsIgnoredKeepsStatusAndRecordsEvent` (Theory: `Succeeded`+`Failed` (B1), `Failed`+`Succeeded` (B2), `Succeeded`+`Succeeded`, `Failed`+`Failed`): `Ignored`, status unchanged, one record.
  - `ExecuteAsync_KnownEventIdSamePayload_ReturnsDuplicateAndChangesNothing` (A4).
  - `ExecuteAsync_KnownEventIdDifferentPayload_ReturnsDuplicatePayloadMismatchAndChangesNothing` (Theory: other status; other paymentId) (B4).
  - `ExecuteAsync_KnownEventIdWithoutHash_ReturnsDuplicate` (Theory: same and other payload): rows from before this spec. The seeded event is `new ProcessedWebhookEvent(eventId, paymentId, null)` (the parameter is `string?`, so no `null!` is needed; `null!` also compiles and works).
  - `ExecuteAsync_RecordReturnsDuplicateEvent_ReturnsDuplicate`: concurrent duplicate path.
  - `ExecuteAsync_RecordReturnsPaymentChanged_LoadsAgainAndIgnoresEvent`: first `RecordAsync` gives `PaymentChanged` and the hook sets the payment to the other terminal status; second attempt gives `Ignored`, two record attempts, final record has this eventId (C1 logic).
  - `ExecuteAsync_RecordReturnsPaymentChangedEveryTime_ThrowsInvalidOperationException`: exactly `MaxAttempts` attempts.
  - `ExecuteAsync_UnknownPayment_...`, `ExecuteAsync_InvalidRequest_...`: kept.

Integration (`ProviderWebhookTests`)
- `Webhook_ValidSignedEvent_Returns200AndUpdatesStatus`: extend to assert the stored `PayloadHash` equals `WebhookPayloadHash.Compute(...)` (A1, A2).
- `Webhook_TerminalPaymentNewEvent_Returns200KeepsStatusAndRecordsEvent` (Theory: `succeeded` then `failed`; `failed` then `succeeded`) (B1, B2): 200, empty body, status unchanged, new event row.
- `Webhook_IgnoredEventRepeated_Returns200AndChangesNothing` (B3): one row, status unchanged.
- `Webhook_SameEventSamePayload_Returns200AndLogsNoWarning` (A4).
- `Webhook_DuplicateEventId_Returns200AndDoesNotChangeStatusAgain`: kept.
- `Webhook_SameEventIdDifferentPayload_Returns200ChangesNothingAndLogsWarning` (Theory: other status; other paymentId (a second seeded payment that must stay `Pending`)) (B4): uses `CapturingLoggerProvider`; one `Warning` entry in the category above that contains the eventId.
- `Webhook_EventStoredWithoutPayloadHash_CountsAsDuplicate`: event row inserted, `PayloadHash` set to NULL with SQL; same eventId with the other status -> 200, status unchanged, one row, no warning.
- `Webhook_TerminalPaymentRejectedRequest_ReturnsSpec002ResponseAndChangesNothing` (Theory: wrong signature -> 401; `"pending"` status -> 400 `status` error (A5, D1); empty eventId -> 400) (A3): payment in `Succeeded`, status unchanged, no new row.
- `Webhook_ConcurrentTerminalEvents_KeepFirstSavedStatusAndIgnoreOther` (Theory: `succeeded` saves first; `failed` saves first) (C1): `WithWebHostBuilder(ConfigureTestServices)` wraps `IPaymentRepository` and `IWebhookEventRepository` with test decorators over the real `PaymentRepository` / `WebhookEventRepository`. The payment decorator holds the first `FindAsync` of each request until both requests have loaded the payment. The event decorator holds the second event's first `RecordAsync` until the first event's `RecordAsync` has returned. Expect: both 200, status = first event's status, both event rows stored. Without the `xmin` token the second save overwrites the status, so the test fails. Each request reads the payment with its own scoped `AppDbContext` and its own connection, so each read runs in that request's own database transaction (an implicit one; see the C1 decision in section 10).
- `Webhook_ConcurrentDuplicates_ProcessOnce`: kept (C2).
- `RecordAsync_EventIdAlreadyStored_ReturnsDuplicateEventAndSavesNothing`: replaces `TryRecordAsync_...` (A6: no status change without its event).
- `RecordAsync_PaymentChangedSinceLoad_ReturnsPaymentChangedAndSavesNothing`: two scopes load the same `Pending` payment; scope 1 `TryChangeStatus(Succeeded)` + `RecordAsync` -> `Recorded`; scope 2 `TryChangeStatus(Failed)` + `RecordAsync` -> `PaymentChanged`; DB: `Succeeded`, scope 2 event not stored (A6: no event without its status change; proves the token).
- `RecordAsync_PaymentNotTracked_ThrowsInvalidOperationException`: payment created with `new`, not loaded.

Integration (`DatabaseInitializerTests`, same collection, so it runs serially)
- `InitializeAsync_PayloadHashColumnMissing_AddsNullableColumnAndKeepsRows`: seed an event, `ALTER TABLE ... DROP COLUMN "PayloadHash"`, run `InitializeAsync`; the column exists, is nullable, and the old row is still there with NULL.
- `InitializeAsync_RunTwice_DoesNotFail`.

## 4. Edge cases

| Edge case (spec) | Test |
|---|---|
| A1/A2 Pending -> terminal | `ExecuteAsync_PendingPayment_...`, `Webhook_ValidSignedEvent_...` |
| A3 rejected request on a terminal payment | `Webhook_TerminalPaymentRejectedRequest_...`, existing spec 002 tests |
| A4 same event, same payload | `ExecuteAsync_KnownEventIdSamePayload_...`, `Webhook_SameEventSamePayload_...` |
| A5 `pending` for a `Succeeded` payment | `Webhook_TerminalPaymentRejectedRequest_...` (pending row), `TryChangeStatus_FromTerminal_...` (Pending target) |
| A6 event and status in one transaction | `RecordAsync_EventIdAlreadyStored_...`, `RecordAsync_PaymentChangedSinceLoad_...` |
| B1 Failed after Succeeded (D2) | `ExecuteAsync_TerminalPayment_...`, `Webhook_TerminalPaymentNewEvent_...` |
| B2 Succeeded after Failed (D3) | same two tests |
| Same terminal status again, new eventId | `ExecuteAsync_TerminalPayment_...` |
| B3 ignored event repeated (D4) | `Webhook_IgnoredEventRepeated_...` |
| B4 same eventId, other payload (D5) | `ExecuteAsync_KnownEventIdDifferentPayload_...`, `Webhook_SameEventIdDifferentPayload_...` |
| Event stored before this spec (no hash) | `ExecuteAsync_KnownEventIdWithoutHash_...`, `Webhook_EventStoredWithoutPayloadHash_...` |
| Existing database without the new column | `InitializeAsync_PayloadHashColumnMissing_...` |
| C1 concurrent different terminal events | `Webhook_ConcurrentTerminalEvents_...`, `ExecuteAsync_RecordReturnsPaymentChanged_...` |
| C2 concurrent copies of one event | `Webhook_ConcurrentDuplicates_ProcessOnce`, `ExecuteAsync_RecordReturnsDuplicateEvent_...` |
| Attempts used up | `ExecuteAsync_RecordReturnsPaymentChangedEveryTime_...` |
| Payment not tracked by the context | `RecordAsync_PaymentNotTracked_...` |

## 5. Pattern

None. The transition rule is one check in `Payment.TryChangeStatus`; a state machine type would add code without solving a problem.

## 6. Data and schema

- Existing payments: stored statuses do not change. A payment that spec 002 moved `Succeeded` -> `Failed` (or the reverse) keeps its stored status, and that status is now final.
- Existing event rows: they have no `PayloadHash` (NULL). They still count as duplicates; no hash compare, no warning. EF Core materializes them with `PayloadHash == null`, which the nullable constructor parameter and property allow.
- Schema change: table `ProcessedWebhookEvents`, new column `PayloadHash character varying(64) NULL`. A new database gets it from `EnsureCreated`. An existing database gets it from `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` in `DatabaseInitializer` at start. No data is lost; old rows get NULL.
- Concurrency token: `Payments.xmin` (a PostgreSQL system column). No schema change.

## 7. Atomic operations

1. **Record the event and the status change** (applied or ignored).
   - Owner: `WebhookEventRepository.RecordAsync`.
   - Boundary: one `SaveChangesAsync` on the scoped `AppDbContext` (one implicit transaction): the event INSERT and, when the status changed, the payment UPDATE with `WHERE xmin = <loaded value>`.
   - Shared state: the payment is tracked by the same scoped `AppDbContext` because `PaymentRepository.FindAsync` loaded it in the same scope. `RecordAsync` checks this (throws when detached) and the interface comment states it. On `DuplicateEvent` or `PaymentChanged` it clears the change tracker, which also drops the payment that `PaymentRepository` loaded; the use case loads it again.
2. **Read, evaluate, save for one event** (C1).
   - Owner: `ProcessProviderWebhookUseCase.ExecuteAsync`.
   - Boundary: not one database transaction. The read runs in its own implicit transaction on the request's own scoped context; the save runs in a second implicit transaction (operation 1). Consistency comes from the optimistic token in operation 1: the `xmin` value from the read is checked in the save, so a stale read fails the save, nothing is saved, and the use case evaluates the event again on fresh data (at most `MaxAttempts`). This gives the same result as an explicit transaction that spans the read and the save (see the C1 decision in section 10).
3. `DatabaseInitializer.InitializeAsync`: two idempotent statements, no shared transaction needed.

## 8. Public contract decisions

- Route, request fields, validation, 401/415/400/404 and their bodies: unchanged (spec 002 contract).
- An ignored event: 200 with no body (D6). A duplicate with a different payload: 200 with no body (D5).
- `pending` stays rejected with 400 `Status has an invalid value.` (D1).
- Stored format of `PayloadHash`: lower-case hex SHA-256 over UTF-8 `"{paymentId:D}|{ProviderPaymentStatus member name}"`, 64 characters. Reason: "same payload" means the same `paymentId` and `status`. JSON formatting and status casing (`"FAILED"` vs `"failed"`) do not change the hash. NULL only for rows stored before this spec.
- Warning log text and category as in section 2. Only the eventId is logged, not the payload.
- 500 problem when the use case uses up its attempts. This cannot happen with the current rules (see section 2), so it is a safety net and not a normal response.

## 9. Implementation choices

- `TryChangeStatus` replaces `ChangeStatus`, so a caller cannot change a status without the rule.
- `ProcessedWebhookEvent` takes `string? payloadHash` without a null guard. The type then states the real stored data (a pre-spec-003 row has no hash), and a test can build such a row without `null!`. The use case always passes a computed hash.
- Optimistic concurrency with `xmin` as a shadow property (the Domain stays without persistence fields). If `EnsureCreated` tries to create a `Version` column, map it explicitly with `HasColumnName("xmin").HasColumnType("xid")`.
- No explicit `BeginTransaction` around the read and the save; no transaction port in Application (see the C1 decision in section 10).
- The warning is logged in the endpoint, from the `DuplicatePayloadMismatch` outcome. Application cannot reference `Microsoft.Extensions.Logging` (architecture: Domain and FluentValidation only).
- `ExistsAsync` is removed because `FindAsync` gives the hash. `FindAsync` uses `AsNoTracking`.
- No outcome column (applied/ignored) on the event row; the spec does not need it.
- `DatabaseInitializer` is in Infrastructure, because it uses raw SQL against the EF Core model's table.
- Test seams for C1 are decorators over the Application ports, registered with `ConfigureTestServices`. No production hooks.

## 10. Flagged decisions (Decisions)

The user decisions D1–D6 are applied as written. The decisions below are not answered by the spec:

- `FLAGGED` **Hash input.** The hash covers `paymentId` and `status` (semantic payload), not the raw body bytes. Alternative: hash the raw body. Effect: a resend with other whitespace, field order or status casing would then log a mismatch warning although it means the same.
- `FLAGGED` **Rows without a hash.** A duplicate of an event stored before this spec never logs a warning, even when its stored `paymentId` differs. Alternative: compare the stored `PaymentId` for such rows and warn on a difference. Effect: a partial detection for old rows (status differences still not seen).
- **Nullable hash in the Domain constructor** (test-writer PLAN UPDATE NEEDED, loop 1). `ProcessedWebhookEvent` takes `string? payloadHash` with no `ThrowIfNull`. Reason: the property is `string?` because pre-spec-003 rows have no hash, and the unit test `ExecuteAsync_KnownEventIdWithoutHash_ReturnsDuplicate` must build such a row. Not flagged: no client or database effect (the column was already nullable in this plan).
- `FLAGGED` **Concurrent duplicate with another payload.** When the unique key rejects the insert (`DuplicateEvent`), the result is `Duplicate` without a hash compare and without a warning. Alternative: load the stored event and compare. Effect: a warning also in this rare race, with one more query.
- `FLAGGED` **Concurrency control.** Optimistic (`xmin` token) and one more evaluation after a conflict. Alternative: pessimistic lock (`SELECT ... FOR UPDATE` in an explicit transaction). Effect: no retry, but the second request waits on the lock, and the spec's C1 test shape (both read before either saves) is not possible.
- `FLAGGED` **C1 test requirement "inside their own transaction"** (test-writer note, loop 1). Decision: the design and the test do not change; the current design satisfies the requirement. Reasons:
  1. Each request has its own scoped `AppDbContext` and its own connection. Its read of the payment runs in its own database transaction (PostgreSQL runs every statement outside an explicit transaction in an implicit one). The two requests never share a transaction or a context.
  2. The requirement's purpose is that both requests have read the same `Pending` state before either saves, so that both decide on stale data. The C1 test forces exactly this with the decorators (both `FindAsync` calls return before either `RecordAsync` runs).
  3. The test can fail: without the `xmin` token the second save overwrites the first terminal status, and the test sees the wrong status.
  4. An explicit transaction from the read to the save (at PostgreSQL's default READ COMMITTED level) would not change the result: the UPDATE still checks `xmin`, and the conflict is detected in the same way. It would only add a transaction port in Application (or a transaction in the endpoint) and more code (KISS, YAGNI).
  Alternative: open an explicit transaction per request that spans the read and the save (for example a `IUnitOfWork.BeginAsync` port, or REPEATABLE READ). Effect: the test matches the spec's words literally; more code; with REPEATABLE READ the conflict shows as a serialization failure (SQLSTATE 40001) instead of `DbUpdateConcurrencyException`, so `RecordAsync` must map that error too. Same public behavior.
- `FLAGGED` **Schema upgrade.** An idempotent `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` after `EnsureCreated`, instead of EF Core migrations. Alternative: migrations. Effect: a design package, generated files, and a baseline for databases that `EnsureCreated` made; more code than this one column needs. Switch when a spec needs more schema changes on kept data.
- `FLAGGED` **Same terminal status again with a new eventId** (for example `succeeded` for a `Succeeded` payment): `Ignored`, recorded, 200. No difference from "applied" that a client or the database can see.
