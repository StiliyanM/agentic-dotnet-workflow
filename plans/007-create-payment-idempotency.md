# Plan 007-create-payment-idempotency

`POST /payments` requires an `Idempotency-Key` header (1–100 characters, case-sensitive). The payment and its key record are stored in one transaction. A repeat with the same key and the same request replays the first response. The same key with a different request gives 422. A request that overlaps with another request for the same key gives 409. A key expires 24 hours after it was stored, and then it can be used again. The request fields, the 201 body and the webhook contract do not change.

## 1. Files

Domain (`src/AgenticPayments.Domain/`)
- `Payments/IdempotencyRecord.cs` (new): stored key, request hash, payment id, creation time, expiry rule, renewal.

Application (`src/AgenticPayments.Application/`)
- `Payments/IdempotencyKeyValidator.cs` (new): FluentValidation rule for the key (required, max 100).
- `Payments/CreatePaymentRequestHash.cs` (new): hash of the parsed `amount`, `currency` and `method`.
- `Payments/IIdempotencyRecordRepository.cs` (new): port. Finds a record and saves a payment with its record as one unit.
- `Payments/IdempotencySaveResult.cs` (new): result enum of `SaveAsync`.
- `Payments/CreatePaymentOutcome.cs` (new): outcome enum.
- `Payments/CreatePaymentResult.cs`: add `Outcome` and the factories `IdempotencyKeyReused()` and `IdempotencyKeyInProgress()`.
- `Payments/CreatePaymentUseCase.cs`: new constructor and `ExecuteAsync(idempotencyKey, request, ct)`. Adds the key lookup, replay, mismatch, renewal and conflict logic.
- `Payments/IPaymentRepository.cs`: remove `AddAsync` (it has no caller now; the payment is saved by `IIdempotencyRecordRepository.SaveAsync`).
- `ApplicationServiceCollectionExtensions.cs`: register `TimeProvider.System` as a singleton.

Infrastructure (`src/AgenticPayments.Infrastructure/`)
- `Persistence/AppDbContext.cs`: add `DbSet<IdempotencyRecord> IdempotencyRecords`.
- `Persistence/IdempotencyRecordConfiguration.cs` (new): key `Key` (max 100), `RequestHash` (max 64, required), FK `PaymentId` to `Payments` with cascade delete, `xmin` concurrency token.
- `Persistence/IdempotencyRecordRepository.cs` (new): implements the port.
- `Persistence/PaymentRepository.cs`: remove `AddAsync`.
- `Persistence/DatabaseInitializer.cs`: add idempotent `CREATE TABLE IF NOT EXISTS` and `CREATE INDEX IF NOT EXISTS` for `IdempotencyRecords`.
- `InfrastructureServiceCollectionExtensions.cs`: register `IIdempotencyRecordRepository` (scoped).

Api (`src/AgenticPayments.Api/`)
- `Payments/PaymentEndpoints.cs`: read the header, pass it to the use case, and map the 4 outcomes.

Tests
- `tests/AgenticPayments.UnitTests/Payments/IdempotencyRecordTests.cs` (new).
- `tests/AgenticPayments.UnitTests/Payments/CreatePaymentRequestHashTests.cs` (new).
- `tests/AgenticPayments.UnitTests/Payments/IdempotencyKeyValidatorTests.cs` (new).
- `tests/AgenticPayments.UnitTests/Payments/FakeIdempotencyRecordRepository.cs` (new). It holds seedable records by key, the saved (record, payment) pairs, and a configurable result for the next `SaveAsync`.
- `tests/AgenticPayments.UnitTests/Payments/FakeTimeProvider.cs` (new). A `TimeProvider` subclass with a settable `GetUtcNow()`. No new package.
- `tests/AgenticPayments.UnitTests/Payments/CreatePaymentUseCaseTests.cs`: new constructor and signature; new tests.
- `tests/AgenticPayments.UnitTests/Payments/FakePaymentRepository.cs`: remove `AddAsync`, and keep `Added` as the seed list.
- `tests/AgenticPayments.IntegrationTests/CreatePaymentTests.cs`: every request sends a new unique `Idempotency-Key`. Without the key the existing tests now get 400.
- `tests/AgenticPayments.IntegrationTests/CreatePaymentIdempotencyTests.cs` (new).
- `tests/AgenticPayments.IntegrationTests/Infrastructure/RacingIdempotencyRecordRepository.cs` and `IdempotencyRaceCoordinator.cs` (new). Test decorator for C1.
- `tests/AgenticPayments.IntegrationTests/Infrastructure/RacingPaymentRepository.cs`: remove `AddAsync`.
- `tests/AgenticPayments.IntegrationTests/DatabaseInitializerTests.cs`: add the table-upgrade test, and restore the table in `DisposeAsync`.

## 2. Public types and signatures

### Domain
```csharp
namespace AgenticPayments.Domain.Payments;

public sealed class IdempotencyRecord(string key, string requestHash, Guid paymentId, DateTimeOffset createdAt)
{
    public const int MaxKeyLength = 100;
    public const int RequestHashLength = 64;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public string Key { get; private set; }               // = key (case-sensitive, stored as given)
    public string RequestHash { get; private set; }       // = requestHash
    public Guid PaymentId { get; private set; }           // = paymentId
    public DateTimeOffset CreatedAt { get; private set; } // = createdAt

    // true when now >= CreatedAt + Lifetime.
    public bool IsExpired(DateTimeOffset now);

    // Starts a new period for an expired key: sets RequestHash, PaymentId and CreatedAt = now.
    // Throws InvalidOperationException when !IsExpired(now). A live key is never reassigned.
    // ArgumentException.ThrowIfNullOrEmpty(requestHash).
    public void Renew(string requestHash, Guid paymentId, DateTimeOffset now);
}
```
The constructor guards are `ArgumentException.ThrowIfNullOrEmpty` for `key` and `requestHash`. They are in property initializers or in a small guard helper. EF Core binds the constructor parameters by name.

### Application
```csharp
namespace AgenticPayments.Application.Payments;

// Error key "Idempotency-Key". CascadeMode.Stop.
// Empty or whitespace-only -> "Idempotency-Key header is required."
// Length > IdempotencyRecord.MaxKeyLength -> "Idempotency-Key header must be at most 100 characters."
public sealed class IdempotencyKeyValidator : AbstractValidator<string>;   // RuleFor(k => k)...OverridePropertyName("Idempotency-Key")

public static class CreatePaymentRequestHash
{
    // Lower-case hex SHA-256 over UTF-8 of $"{amount:F2 invariant}|{currency}|{method}" (enum member names). 64 chars.
    // Call only after validation (amount has at most 2 decimals), so 10.5 and 10.50 give the same hash.
    public static string Compute(decimal amount, Currency currency, PaymentMethod method);
}

public enum IdempotencySaveResult { Saved, KeyConflict }

public interface IIdempotencyRecordRepository
{
    // The record for this exact key (case-sensitive), or null. Expired records are returned too.
    // The record is tracked by this scope's context, so that SaveAsync can save a Renew on it.
    Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken);

    // One SaveChanges (one transaction): inserts the payment, and inserts the record (when it is not tracked)
    // or updates it (when FindAsync of this scope returned it; its xmin token is checked).
    // Saved: both stored. KeyConflict: another request stored or renewed this key first (unique violation or
    // changed row); nothing stored; the context forgets all tracked entities.
    Task<IdempotencySaveResult> SaveAsync(IdempotencyRecord record, Payment payment, CancellationToken cancellationToken);
}

public interface IPaymentRepository
{
    Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken);   // AddAsync removed
}

public enum CreatePaymentOutcome { Success, Invalid, IdempotencyKeyReused, IdempotencyKeyInProgress }

public sealed class CreatePaymentResult
{
    public CreatePaymentOutcome Outcome { get; }
    public CreatePaymentResponse? Response { get; }          // not null only for Success
    public IDictionary<string, string[]> Errors { get; }     // empty unless Invalid
    [MemberNotNullWhen(true, nameof(Response))]
    public bool IsSuccess { get; }                           // Outcome == Success
    public static CreatePaymentResult Success(CreatePaymentResponse response);
    public static CreatePaymentResult Invalid(IDictionary<string, string[]> errors);
    public static CreatePaymentResult IdempotencyKeyReused();
    public static CreatePaymentResult IdempotencyKeyInProgress();
}

public sealed class CreatePaymentUseCase(
    IValidator<CreatePaymentRequest> validator,
    IValidator<string> idempotencyKeyValidator,
    IIdempotencyRecordRepository idempotencyRecords,
    TimeProvider timeProvider)
{
    public Task<CreatePaymentResult> ExecuteAsync(string idempotencyKey, CreatePaymentRequest request, CancellationToken cancellationToken);
}
```
`CreatePaymentRequest`, `CreatePaymentResponse`, `CreatePaymentValidator`: no change.

Use case order:
1. `ThrowIfNull(idempotencyKey)`, `ThrowIfNull(request)`.
2. Validate the key and the request. Merge the errors (key errors and field errors). If there are errors, return `Invalid(errors)`. Nothing is read or stored (A5).
3. `hash = CreatePaymentRequestHash.Compute(...)`, `now = timeProvider.GetUtcNow()`.
4. `record = FindAsync(key)`. If it is not null and `!record.IsExpired(now)`: when `record.RequestHash == hash`, return `Success(new CreatePaymentResponse(record.PaymentId, redirectUrl(request.Method, record.PaymentId), PaymentStatus.Pending))` (replay, nothing saved). Otherwise return `IdempotencyKeyReused()`.
5. `payment = new Payment(...)`. If `record` is null, make `new IdempotencyRecord(key, hash, payment.Id, now)`. Otherwise call `record.Renew(hash, payment.Id, now)`.
6. `SaveAsync(record, payment)`: `Saved` returns `Success(new response)`. `KeyConflict` returns `IdempotencyKeyInProgress()` (no retry).

### Infrastructure
```csharp
namespace AgenticPayments.Infrastructure.Persistence;
public sealed class IdempotencyRecordRepository(AppDbContext db) : IIdempotencyRecordRepository;
public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>;
// AppDbContext: public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
```
- `SaveAsync`: `db.Payments.Add(payment)`. If `db.Entry(record).State == Detached`, call `db.IdempotencyRecords.Add(record)`. Otherwise the record is already tracked and modified. Then `SaveChangesAsync`. On `DbUpdateConcurrencyException` (caught first): `ChangeTracker.Clear()` and return `KeyConflict`. On `DbUpdateException` with `PostgresException { SqlState: UniqueViolation }`: `ChangeTracker.Clear()` and return `KeyConflict`.
- `DatabaseInitializer.InitializeAsync` (signature unchanged): after the existing statements it runs
  `CREATE TABLE IF NOT EXISTS "IdempotencyRecords" ("Key" character varying(100) NOT NULL, "RequestHash" character varying(64) NOT NULL, "PaymentId" uuid NOT NULL, "CreatedAt" timestamp with time zone NOT NULL, CONSTRAINT "PK_IdempotencyRecords" PRIMARY KEY ("Key"), CONSTRAINT "FK_IdempotencyRecords_Payments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "Payments" ("Id") ON DELETE CASCADE)`
  and `CREATE INDEX IF NOT EXISTS "IX_IdempotencyRecords_PaymentId" ON "IdempotencyRecords" ("PaymentId")`. These must match what `EnsureCreated` makes from the model.

### HTTP: `POST /payments`
- Request: header `Idempotency-Key: <1–100 chars>`. Body unchanged: `{ "amount", "currency", "method" }`.
- Order: (1) the body is read as before: 415 for non-JSON, and 400 problem or 400 validation problem for unreadable or unbindable JSON. The key is not checked yet. (2) The key and the field validation run together and give one 400 validation problem with all errors. (3) The idempotency logic runs.

| Case | Status | Body |
|---|---|---|
| New key, or expired key | 201 | `{ paymentId, redirectUrl, status: "Pending" }` (unchanged) |
| Same key, same request, not expired | 201 | the first response: same `paymentId`, same `redirectUrl`, `status: "Pending"` |
| Header missing, empty or whitespace-only | 400 | validation problem, `errors["Idempotency-Key"] = ["Idempotency-Key header is required."]` |
| Key longer than 100 | 400 | validation problem, `errors["Idempotency-Key"] = ["Idempotency-Key header must be at most 100 characters."]` |
| Same key, different request | 422 | problem (`application/problem+json`), `status: 422`, `detail: "The Idempotency-Key was already used with a different request."` |
| Same key, overlapping request saved first | 409 | problem, `status: 409`, `detail: "Another request with the same Idempotency-Key was processed at the same time. Retry the request."` |

Header constant in Api: `PaymentEndpoints.IdempotencyKeyHeader = "Idempotency-Key"` (public const). The endpoint reads `httpRequest.Headers[IdempotencyKeyHeader].ToString()`. A missing header gives `""`.

`POST /webhooks/provider`: no change.

## 3. Tests

Unit
- `IdempotencyRecordTests.Constructor_SetsAllValues`.
- `IdempotencyRecordTests.IsExpired_BeforeLifetime_ReturnsFalse` (now = CreatedAt + 24h − 1 tick).
- `IdempotencyRecordTests.IsExpired_AtOrAfterLifetime_ReturnsTrue` (Theory: +24h, +25h).
- `IdempotencyRecordTests.Renew_Expired_SetsHashPaymentIdAndCreatedAt`.
- `IdempotencyRecordTests.Renew_NotExpired_ThrowsInvalidOperationException`: the values do not change.
- `CreatePaymentRequestHashTests.Compute_KnownRequest_ReturnsLowercaseSha256OfCanonicalString`: equals the SHA-256 of `"10.50|Eur|Ideal"`, 64 chars.
- `CreatePaymentRequestHashTests.Compute_SameAmountOtherScale_ReturnsSameHash` (10.5m and 10.50m).
- `CreatePaymentRequestHashTests.Compute_DifferentAmountCurrencyOrMethod_ReturnsDifferentHash` (Theory).
- `IdempotencyKeyValidatorTests.Validate_EmptyOrWhitespace_ReturnsRequiredError` (Theory: `""`, `" "`): key `Idempotency-Key`.
- `IdempotencyKeyValidatorTests.Validate_LongerThan100_ReturnsLengthError` (101 chars).
- `IdempotencyKeyValidatorTests.Validate_1To100Characters_IsValid` (Theory: 1, 100 chars, UUID, mixed case).
- `CreatePaymentUseCaseTests` (with fakes):
  - `ExecuteAsync_ValidRequest_ReturnsPendingResponseWithRedirectUrl` (kept, new signature).
  - `ExecuteAsync_NewKey_SavesPaymentWithRecord` (A1): one save with the right payment and the record (key, `RequestHash == Compute(...)`, `PaymentId == payment.Id`, `CreatedAt == now`).
  - `ExecuteAsync_SameKeySameRequest_ReturnsFirstResponseAndSavesNothing` (A2): same id and URL, `Status == Pending`, no save.
  - `ExecuteAsync_SameKeyDifferentRequest_ReturnsIdempotencyKeyReusedAndSavesNothing` (A3; Theory: amount, currency, method).
  - `ExecuteAsync_InvalidKey_ReturnsInvalidAndDoesNotTouchRepository` (A4; Theory: `""`, `" "`, 101 chars): no lookup, no save.
  - `ExecuteAsync_InvalidKeyAndInvalidRequest_ReturnsBothErrors`.
  - `ExecuteAsync_InvalidRequest_ReturnsErrorsAndSavesNothing` (A5, kept): no lookup, no save.
  - `ExecuteAsync_ExpiredKey_RenewsRecordAndCreatesNewPayment` (A6): a new payment id, the same record instance renewed, one save. Theory: same and different request.
  - `ExecuteAsync_KeyJustBeforeExpiry_ReplaysFirstResponse` (24h − 1 tick).
  - `ExecuteAsync_SaveReturnsKeyConflict_ReturnsIdempotencyKeyInProgress` (C1 logic).

Integration (`CreatePaymentIdempotencyTests`, collection `ApiCollection`; cleans up its payments, and the records go with them by cascade)
- `CreatePayment_NewKey_Returns201AndStoresKey` (A1): one payment, and one `IdempotencyRecords` row with the key, the payment id and the hash.
- `CreatePayment_SameKeySameRequest_Returns201WithSamePaymentIdAndRedirectUrl` (A2; Theory over the second body: identical; currency and method in another case; `10.5` vs `10.50`; other field order and whitespace): same `paymentId` and `redirectUrl`, one payment.
- `CreatePayment_SameKeyDifferentRequest_Returns422AndCreatesNothing` (A3; Theory: amount, currency, method): problem body with status 422 and the detail text; the payment count is unchanged; the record is unchanged.
- `CreatePayment_MissingOrEmptyKey_Returns400WithKeyError` (A4; Theory: no header, `""`, `"   "`): validation problem with only the `Idempotency-Key` error; no payment.
- `CreatePayment_KeyLongerThan100_Returns400` and `CreatePayment_KeyOf100Characters_Returns201`.
- `CreatePayment_MissingKeyAndInvalidField_Returns400WithBothErrors`.
- `CreatePayment_InvalidRequestThenValidRequestSameKey_Returns400Then201` (A5): no record after the 400, then exactly one payment.
- `CreatePayment_SameKeyAfter24Hours_Returns201WithNewPayment` (A6): set `CreatedAt` of the record to now − 24h − 1 min with SQL; the response has a new `paymentId`; the record now points to it with a new `CreatedAt`; the old payment still exists.
- `CreatePayment_KeysDifferOnlyInCase_CreatesTwoPayments`.
- `CreatePayment_ReplayAfterStatusChange_ReturnsFirstResponseWithPending`: set the payment status to `Succeeded` with SQL; the replay gives 201 with `status: "Pending"`.
- `CreatePayment_ConcurrentSameKey_CreatesOnePayment` (C1). `WithWebHostBuilder(ConfigureTestServices)` replaces `IIdempotencyRecordRepository` with `RacingIdempotencyRecordRepository` over the real `IdempotencyRecordRepository`. The first `FindAsync` of each request waits until both requests have done their lookup. The second `SaveAsync` waits until the first `SaveAsync` has returned. Two identical requests with key K and a unique amount. Expect exactly one payment with that amount. The statuses are {201, 201 with the same `paymentId`} or {201, 409}; this design gives 201 and 409. Without the primary key on `Key`, both inserts succeed and the test sees two payments.
- `SaveAsync_KeyAlreadyStored_ReturnsKeyConflictAndSavesNothing`: scope 2 saves a new record with a key that is already stored. The result is `KeyConflict`, and scope 2's payment is not stored.
- `SaveAsync_ExpiredRecordRenewedByOtherScope_ReturnsKeyConflictAndSavesNothing`: two scopes load the same expired record and both call `Renew` and `SaveAsync`. The first gives `Saved`, the second gives `KeyConflict`. Only the first new payment exists, and the record points to it (`xmin` token).

Integration (`DatabaseInitializerTests`)
- `InitializeAsync_IdempotencyTableMissing_CreatesTable`: `DROP TABLE "IdempotencyRecords"`, then `InitializeAsync`. The table exists, and the EF Core model can insert and read a record. `DisposeAsync` runs `InitializeAsync` again, so the other tests keep the table.
- `InitializeAsync_RunTwice_DoesNotFail` (kept): now it also covers the new statements.

Updated: all `CreatePaymentTests` send a unique `Idempotency-Key` (for example `Guid.NewGuid().ToString()`).

## 4. Edge cases

| Edge case | Test |
|---|---|
| A1 new key | `ExecuteAsync_NewKey_...`, `CreatePayment_NewKey_...` |
| A2 same key, same request (with other JSON format and case) | `ExecuteAsync_SameKeySameRequest_...`, `CreatePayment_SameKeySameRequest_...`, `Compute_SameAmountOtherScale_...` |
| A3 same key, different request | `ExecuteAsync_SameKeyDifferentRequest_...`, `CreatePayment_SameKeyDifferentRequest_...` |
| A4 missing, empty or whitespace key | `Validate_EmptyOrWhitespace_...`, `ExecuteAsync_InvalidKey_...`, `CreatePayment_MissingOrEmptyKey_...` |
| Key length 1 / 100 / 101 | `Validate_1To100Characters_...`, `Validate_LongerThan100_...`, `CreatePayment_KeyLongerThan100_...`, `CreatePayment_KeyOf100Characters_...` |
| Key is case-sensitive | `CreatePayment_KeysDifferOnlyInCase_...` |
| A5 invalid request does not store the key | `ExecuteAsync_InvalidRequest_...`, `CreatePayment_InvalidRequestThenValidRequestSameKey_...` |
| A6 key older than 24h | `IsExpired_...`, `Renew_...`, `ExecuteAsync_ExpiredKey_...`, `CreatePayment_SameKeyAfter24Hours_...` |
| Just before expiry | `ExecuteAsync_KeyJustBeforeExpiry_...` |
| C1 concurrent identical requests | `CreatePayment_ConcurrentSameKey_...`, `ExecuteAsync_SaveReturnsKeyConflict_...`, `SaveAsync_KeyAlreadyStored_...` |
| Concurrent renewal of an expired key | `SaveAsync_ExpiredRecordRenewedByOtherScope_...` |
| Replay after a webhook changed the status | `CreatePayment_ReplayAfterStatusChange_...` |
| Missing key and invalid field together | `ExecuteAsync_InvalidKeyAndInvalidRequest_...`, `CreatePayment_MissingKeyAndInvalidField_...` |
| Existing database without the table | `InitializeAsync_IdempotencyTableMissing_...` |
| Webhook contract unchanged | existing `ProviderWebhookTests` (no change) |

## 5. Pattern

None.

## 6. Data and schema

- Existing payments (created before this spec) have no key record. They are not changed and cannot be replayed. No backfill.
- New table `IdempotencyRecords`: `Key varchar(100)` PK (case-sensitive by the default deterministic collation), `RequestHash varchar(64) NOT NULL`, `PaymentId uuid NOT NULL` FK to `Payments(Id)` with `ON DELETE CASCADE`, `CreatedAt timestamptz NOT NULL`, and the index `IX_IdempotencyRecords_PaymentId`. The concurrency token is the system column `xmin`, so it adds no column.
- New database: `EnsureCreated` makes the table. Existing database: `DatabaseInitializer` makes it with `CREATE TABLE IF NOT EXISTS` / `CREATE INDEX IF NOT EXISTS` at start. No existing data changes.
- Expired rows stay in the table (cleanup is out of scope). A renewal updates the row in place.
- `RequestHash` stored format: lower-case hex SHA-256 of `"{amount:F2}|{Currency member}|{PaymentMethod member}"`, 64 chars.

## 7. Atomic operations

1. **Store a new payment with its key** (insert, or renewal of an expired key).
   - Owner: `IdempotencyRecordRepository.SaveAsync`.
   - Boundary: one `SaveChangesAsync` on the scoped `AppDbContext`, which is one implicit transaction. It does the payment INSERT plus the record INSERT, or the payment INSERT plus the record UPDATE `WHERE xmin = <loaded>`. Both are stored, or neither is.
   - Shared state: for a renewal, the record must be tracked by the same context. `FindAsync` of the same repository loaded it in this scope, so lookup and save stay in one component. The port comment states this. `SaveAsync` decides insert or update from the tracking state. On `KeyConflict` it clears the change tracker.
2. **Lookup, decide, save for one request.**
   - Owner: `CreatePaymentUseCase.ExecuteAsync`.
   - Boundary: not one transaction. The lookup and the save are separate implicit transactions. Correctness comes from operation 1. The primary key on `Key` rejects a second insert, and the `xmin` token rejects a second renewal. The losing request stores nothing (its payment INSERT rolls back with the record) and gets 409.
3. `DatabaseInitializer.InitializeAsync`: idempotent statements. They need no shared transaction.

## 8. Public contract decisions

- Header name `Idempotency-Key`, required on `POST /payments`. Reason: spec rule.
- 400 for an invalid key is a validation problem with error key `Idempotency-Key` and the messages in section 2. Reason: it is the same 400 shape as field errors, so a client handles one format, and the key names the header the client must fix.
- Order: body read errors (415, malformed JSON, binding errors) come before the key check. Key errors and validator errors are returned together. Reason: the body reader stays unchanged (spec 005 contract); one validation problem lists all fixable errors.
- Whitespace-only key counts as empty (400). Reason: FluentValidation `NotEmpty` semantics; such a key is almost certainly a client bug.
- Replay returns 201 with the stored `paymentId`, the `redirectUrl` built from the same method and id (identical to the first), and `status: "Pending"`. Reason: the spec says "returns the first response".
- 422 problem with the detail `The Idempotency-Key was already used with a different request.` Reason: spec rule.
- 409 problem with the detail `Another request with the same Idempotency-Key was processed at the same time. Retry the request.` It is returned when the save loses to a concurrent request for the same key (unique key or `xmin` conflict). Reason: spec rule. A retry then gets the replay or 422.
- Expiry: a key is expired when `now >= CreatedAt + 24h`. An expired key creates a new payment and the record is overwritten.
- Multiple `Idempotency-Key` header values are joined with `,` (ASP.NET `StringValues.ToString()`) and treated as one key.
- Stored formats: table and columns in section 6, the hash format in section 6.

## 9. Implementation choices

- One port (`IIdempotencyRecordRepository`) owns both the lookup and the atomic save. The tracked record never crosses components. `IPaymentRepository.AddAsync` is removed because nothing calls it any more (YAGNI).
- The key validator is a FluentValidation `AbstractValidator<string>` with `RuleFor(k => k).OverridePropertyName("Idempotency-Key")`. It is registered by `AddValidatorsFromAssembly` and injected as `IValidator<string>`.
- `TimeProvider` (BCL) is injected into the use case so that unit tests can control expiry. `TimeProvider.System` is registered in `AddApplication`. `Payment.CreatedAt` still uses `DateTimeOffset.UtcNow` (no change).
- The amount is formatted with `F2` (invariant culture). The validator guarantees at most 2 decimals, so 10.5 and 10.50 give the same canonical text.
- Renewal updates the row in place (no delete plus insert), so one `SaveChanges` with the `xmin` check covers it.
- The unique-violation catch matches any `UniqueViolation` in that `SaveChanges`. Only the record key can conflict, because payment ids are new GUIDs.
- The redirect URL builder stays a private helper in the use case. Replay and creation both use it.
- The C1 test seam is a test-only decorator over the port, registered with `ConfigureTestServices`. There are no production hooks.

## 10. Flagged decisions (Decisions)

- `FLAGGED` **Overlap gives 409, not a wait-and-replay.** The record and the payment are stored in one transaction, so no "processing" row is ever visible. A concurrent request for the same key is detected at the save (by the primary key, or by `xmin` on renewal), and it gets 409 without retry. In C1 the result is 201 + 409. Alternative: after the conflict, read the record again and replay (201 with the same `paymentId`) or give 422. Effect: the client needs fewer retries; there is a little more code; 409 would then never occur.
- `FLAGGED` **No separate "processing" state.** The spec's "still processing" is the window between the lookup and the commit. Alternative: commit a processing marker first and return 409 when a lookup finds it. Effect: two transactions, and a key that stays stuck in processing (409 for up to 24h) if the process fails between them.
- `FLAGGED` **Replay status is always `Pending`** (the first response), even after a webhook changed the payment. Alternative: load the payment and return its current status. Effect: the replay body differs from the first response.
- `FLAGGED` **Error key `Idempotency-Key`** in the 400 validation problem. It is the header name, not a camelCase field name. Alternative: `idempotencyKey`. Effect: a different key that clients must read.
- `FLAGGED` **Body errors before key errors.** A non-JSON or malformed body gives 415 or 400 without a key error, even when the header is missing. Alternative: check the header first. Effect: a missing key would hide body errors, or the reverse.
- `FLAGGED` **Whitespace-only key is rejected** (400). Alternative: accept it as a 1–100 character key. Effect: `" "` would become a valid key.
- `FLAGGED` **Expiry boundary.** A key is expired at exactly 24h (`>=`). Alternative: `>`. Effect: one instant of difference.
- `FLAGGED` **Multiple header values** are joined with `,` into one key. Alternative: 400 for more than one value. Effect: a rare client error would be reported instead of being treated as a key.
- `FLAGGED` **Schema upgrade with raw idempotent SQL** in `DatabaseInitializer` (like spec 003), not EF Core migrations. Alternative: migrations. Effect: more files and a baseline for existing databases.
- Not flagged (no client or database effect): `IPaymentRepository.AddAsync` is removed; `TimeProvider` is injected.
