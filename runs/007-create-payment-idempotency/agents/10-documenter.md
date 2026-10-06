I updated the documentation for spec 007-create-payment-idempotency. I changed only `README.md` and `docs/user/`. I ran no commands. The test counts come from `runs/007-create-payment-idempotency/verify/07-verify/` (summary.txt, unit.log, integration.log).

## Files changed
- C:\Users\Stiliyan\source\repos\ApmPlayground\README.md
- C:\Users\Stiliyan\source\repos\ApmPlayground\docs\user\payments-api.md
- C:\Users\Stiliyan\source\repos\ApmPlayground\docs\user\provider-webhook.md

## New or changed statements and their sources

### README.md
- **Status table:** new row for 007, marked "Completed". This follows the 002 and 003 rows, which were also marked during their runs before the merge.
  - Source: `PaymentEndpoints.cs`, `CreatePaymentUseCase.cs`.
- **Example curl:** now sends `Idempotency-Key`.
  - Source: `PaymentEndpoints.IdempotencyKeyHeader`.
  - Test: `CreatePaymentIdempotencyTests.PostAsync`.
- **Idempotency summary paragraph:**
  - The header is required, 1 to 100 characters. Source: `IdempotencyKeyValidator.cs`, `IdempotencyRecord.MaxKeyLength`.
  - The same key and request give a replay with the same `paymentId` and `redirectUrl`. Source: `CreatePaymentUseCase.cs`. Test: `CreatePayment_SameKeySameRequest_Returns201WithSamePaymentIdAndRedirectUrl`.
  - A different request with the same key gives 422. Test: `CreatePayment_SameKeyDifferentRequest_Returns422AndCreatesNothing`.
  - A missing or empty key gives 400 with `"Idempotency-Key header is required."` Test: `CreatePayment_MissingOrEmptyKey_Returns400WithKeyError`.
  - After 24 hours the key creates a new payment. Source: `IdempotencyRecord.Lifetime`, `IsExpired`. Test: `CreatePayment_SameKeyAfter24Hours_Returns201WithNewPayment`.
- **Startup:** the API creates the `IdempotencyRecords` table if it does not exist. A database "created by spec 002 or later" does not need to be removed.
  - Source: `DatabaseInitializer.cs`.
  - Test: `InitializeAsync_IdempotencyTableMissing_CreatesTable`.
- **Known limitations:**
  - Keys are kept 24 hours and never deleted (no cleanup). Source: `IdempotencyRecord.cs`. No delete code exists. The spec puts cleanup out of scope.
  - Two requests with the same new key at the same time give 201 and 409, with no wait-and-replay. Source: `IdempotencyRecordRepository.SaveAsync`, `CreatePaymentUseCase.cs`. Test: `CreatePayment_ConcurrentSameKey_CreatesOnePayment`.
  - Payments from before spec 007 have no key and cannot be replayed. Source: the plan, section 6 (no backfill code).
  - A replay always shows `Pending`. Source: `CreatePaymentUseCase.Response`. Test: `CreatePayment_ReplayAfterStatusChange_ReturnsFirstResponseWithPending`.
  - The schema upgrades now include `CREATE TABLE IF NOT EXISTS` and `CREATE INDEX IF NOT EXISTS`. Source: `DatabaseInitializer.cs`.
- **Execution evidence:** 007 ran build, format, unit tests (87/87) and integration tests (90/90) with exit status 0.
  - Source: `evidence.md` (07-verify row, exit 0), `verify/07-verify/summary.txt`, `unit.log` line 10, `integration.log` line 12.

### docs/user/payments-api.md
- **Headers table and example curl.** The key is required, 1 to 100 characters and case-sensitive. An empty or whitespace-only key is rejected.
  - Source: `IdempotencyKeyValidator.cs`.
  - Tests: `IdempotencyKeyValidatorTests`, `CreatePayment_MissingOrEmptyKey_Returns400WithKeyError` (`"   "` case), `CreatePayment_KeysDifferOnlyInCase_CreatesTwoPayments`, `CreatePayment_KeyOf100Characters_Returns201`.
- **`paymentId` row:** a replay returns the id of the first payment. Source: `CreatePaymentUseCase.cs`.
- **New error rows (copied from the code):**
  - 400 required: `"Idempotency-Key header is required."` Source: `IdempotencyKeyValidator.cs`.
  - 400 too long: `"Idempotency-Key header must be at most 100 characters."` Source: `IdempotencyKeyValidator.cs`. Test: `CreatePaymentUseCaseTests` (KeyTooLongMessage).
  - 422 detail: "The Idempotency-Key was already used with a different request." Source: `PaymentEndpoints.cs`.
  - 409 detail: "Another request with the same Idempotency-Key was processed at the same time. Retry the request." Source: `PaymentEndpoints.cs`.
- **Validation keys** now include `Idempotency-Key`. Source: `OverridePropertyName("Idempotency-Key")`.
- **Check order:** body errors (415, unreadable body, a field value that cannot be read) are returned without a key error. Key errors and field rule errors are returned together.
  - Source: `PaymentEndpoints.cs` (the body is read before the use case runs), `CreatePaymentUseCase.cs` (merged dictionaries).
  - Test: `CreatePayment_MissingKeyAndInvalidField_Returns400WithBothErrors`.
- **New Idempotency section:**
  - The payment and the key are saved in one transaction. Source: `IdempotencyRecordRepository.SaveAsync` (one `SaveChangesAsync`).
  - New key, replay, 422 with the record unchanged, and expiry with a new payment while the old payment stays. Tests: `CreatePayment_NewKey_...`, `CreatePayment_SameKeySameRequest_...`, `CreatePayment_SameKeyDifferentRequest_...`, `CreatePayment_SameKeyAfter24Hours_...`.
  - An invalid request does not store the key. Test: `CreatePayment_InvalidRequestThenValidRequestSameKey_Returns400Then201`.
  - Concurrent requests give 201 and 409. Test: `CreatePayment_ConcurrentSameKey_CreatesOnePayment`.
  - After a 409, a retry gets the replay or 422. Source: `CreatePaymentUseCase.cs` logic, because the record is committed by then. No test retries after a 409.
  - "Same request" ignores case, property order, whitespace and amount scale, and is a SHA-256 hash. Source: `CreatePaymentRequestHash.cs`. Tests: the variants of `CreatePayment_SameKeySameRequest_...`.
  - Concurrent renewal of an expired key gives 409. Source: the xmin token in `IdempotencyRecordConfiguration.cs`. Test: `SaveAsync_ExpiredRecordRenewedByOtherScope_ReturnsKeyConflictAndSavesNothing`.
  - Limits: expired keys are not deleted. Pre-007 payments have no key. Multiple header values are joined with `,` (`StringValues.ToString()` in `PaymentEndpoints.cs`). The page says that no test covers the joined values.

### docs/user/provider-webhook.md
- **Example step 1:** `POST /payments` needs `Idempotency-Key`. Source: `PaymentEndpoints.cs`.
- **Database schema section:** the `IdempotencyRecords` table and its index are created when they do not exist. The test list now includes `InitializeAsync_IdempotencyTableMissing_CreatesTable`. Source: `DatabaseInitializer.cs`.
- **Limits, "No migrations":** now names the spec 007 table creation. Source: `DatabaseInitializer.cs`.

## Statements removed because they are no longer true
- README Known limitations: "**Create is not idempotent.** A repeated `POST /payments` creates a second payment. There is no idempotency key."
- README Known limitations: "The only schema upgrade is the `PayloadHash` column, added with one `ALTER TABLE` statement at start." It is replaced by a statement that covers both upgrades.
- README: "**Database created by spec 002.** The API adds the `PayloadHash` column at start." It is replaced by "Database created by spec 002 or later", which also names `IdempotencyRecords`.
- provider-webhook.md: "It creates both tables." It is replaced by "It creates all tables."
- The README curl example without `Idempotency-Key`. That request now returns 400.

## Notes for the documentation check
- `evidence.md` still says "Status: in progress" and has no documenter row. I did not change it.
- The example key value (`6f1c2b7e-...`) and the `paymentId` in the README are only examples. The curl commands were not run as part of the verification.
