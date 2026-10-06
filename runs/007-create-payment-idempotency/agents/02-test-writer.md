I wrote the tests for spec 007-create-payment-idempotency, and I found no PLAN GAP and no PLAN UPDATE NEEDED. Nothing has been compiled or run yet. The build is expected to fail because the production types do not exist yet. `dotnet format whitespace --folder tests` ran with no errors. I changed only files under `tests/` and did not commit.

**Plan check.** Every spec case has a test in the plan, with the same expected behavior: each rule in the Rules section, A1–A6, and C1, including the C1 rule that both requests must pass the key lookup before either saves. The plan resolves "still processing" and whitespace-only keys with decisions it marks FLAGGED. Those decisions stay inside what the spec allows.

**Tests I added beyond the plan's list** (all use only planned signatures):
- `IdempotencyRecordTests.Constructor_EmptyKeyOrHash_ThrowsArgumentException` covers the constructor guards that the plan's signatures define.
- The C1 test also checks that both requests did their key lookup (`coordinator.LookupCount == 2`). This proves the two requests overlapped.
- `InitializeAsync_RunTwice_DoesNotFail` now also checks that the `IdempotencyRecords` table exists.

**Two small departures from the plan:**
- `CreatePayment_ReplayAfterStatusChange_ReturnsFirstResponseWithPending` sets the payment to Succeeded through EF Core (`TryChangeStatus` plus `SaveChanges`), not raw SQL. The effect is the same.
- C1 accepts either outcome the spec allows: two 201 responses with the same `paymentId`, or 201 and 409. When there is a 409, it checks the problem body (status 409 and the detail text).

**New files:**
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Payments\IdempotencyRecordTests.cs`
  - `Constructor_SetsAllValues`
  - `Constructor_EmptyKeyOrHash_ThrowsArgumentException` (Theory)
  - `IsExpired_BeforeLifetime_ReturnsFalse`
  - `IsExpired_AtOrAfterLifetime_ReturnsTrue` (Theory: 24h, 25h)
  - `Renew_Expired_SetsHashPaymentIdAndCreatedAt`
  - `Renew_NotExpired_ThrowsInvalidOperationException`
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Payments\CreatePaymentRequestHashTests.cs`
  - `Compute_KnownRequest_ReturnsLowercaseSha256OfCanonicalString`
  - `Compute_SameAmountOtherScale_ReturnsSameHash`
  - `Compute_DifferentAmountCurrencyOrMethod_ReturnsDifferentHash` (Theory)
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Payments\IdempotencyKeyValidatorTests.cs`
  - `Validate_EmptyOrWhitespace_ReturnsRequiredError` (Theory)
  - `Validate_LongerThan100_ReturnsLengthError`
  - `Validate_1To100Characters_IsValid` (Theory: 1 char, 100 chars, UUID, mixed case)
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Payments\FakeIdempotencyRecordRepository.cs`: holds seeded records by key (case-sensitive) and the saved (record, payment) pairs, takes a queue of results for the next `SaveAsync` calls, and counts `FindCalls` and `SaveCalls`.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Payments\FakeTimeProvider.cs`: a `TimeProvider` subclass with a settable `UtcNow`.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\CreatePaymentIdempotencyTests.cs`
  - `CreatePayment_NewKey_Returns201AndStoresKey`
  - `CreatePayment_SameKeySameRequest_Returns201WithSamePaymentIdAndRedirectUrl` (Theory: identical body; currency and method in other case; 10.5 vs 10.50; other field order and whitespace)
  - `CreatePayment_SameKeyDifferentRequest_Returns422AndCreatesNothing` (Theory: amount, currency, method)
  - `CreatePayment_MissingOrEmptyKey_Returns400WithKeyError` (Theory: no header, `""`, `"   "`)
  - `CreatePayment_KeyLongerThan100_Returns400`
  - `CreatePayment_KeyOf100Characters_Returns201`
  - `CreatePayment_MissingKeyAndInvalidField_Returns400WithBothErrors`
  - `CreatePayment_InvalidRequestThenValidRequestSameKey_Returns400Then201`
  - `CreatePayment_SameKeyAfter24Hours_Returns201WithNewPayment`
  - `CreatePayment_KeysDifferOnlyInCase_CreatesTwoPayments`
  - `CreatePayment_ReplayAfterStatusChange_ReturnsFirstResponseWithPending`
  - `CreatePayment_ConcurrentSameKey_CreatesOnePayment` (C1)
  - `SaveAsync_KeyAlreadyStored_ReturnsKeyConflictAndSavesNothing`
  - `SaveAsync_ExpiredRecordRenewedByOtherScope_ReturnsKeyConflictAndSavesNothing`
  - Cleanup deletes the records by the keys the test used, then the payments by id or by the amounts the test used. It does not rely only on the cascade, so a broken implementation cannot leave rows behind.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\Infrastructure\IdempotencyRaceCoordinator.cs`: the first lookup of each request waits until both requests have looked up the key. The second save waits until the first save has returned. The waits have a 30-second safety limit.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\Infrastructure\RacingIdempotencyRecordRepository.cs`: a scoped decorator over the real `IIdempotencyRecordRepository`. The C1 test registers it with `ConfigureTestServices`.

**Changed files:**
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Payments\CreatePaymentUseCaseTests.cs`: rewritten for the new constructor and `ExecuteAsync(key, request, ct)`.
  - Kept, with the new signature: `ExecuteAsync_ValidRequest_ReturnsPendingResponseWithRedirectUrl`.
  - Kept, renamed and extended: `ExecuteAsync_InvalidRequest_ReturnsErrorsAndSavesNothing`.
  - Replaced: `ExecuteAsync_ValidRequest_AddsPaymentToRepository` is now `ExecuteAsync_NewKey_SavesPaymentWithRecord`.
  - Added: `ExecuteAsync_SameKeySameRequest_ReturnsFirstResponseAndSavesNothing`, `ExecuteAsync_SameKeyDifferentRequest_ReturnsIdempotencyKeyReusedAndSavesNothing` (Theory), `ExecuteAsync_InvalidKey_ReturnsInvalidAndDoesNotTouchRepository` (Theory: `""`, `" "`, 101 chars), `ExecuteAsync_InvalidKeyAndInvalidRequest_ReturnsBothErrors`, `ExecuteAsync_ExpiredKey_RenewsRecordAndCreatesNewPayment` (Theory: same and different request), `ExecuteAsync_KeyJustBeforeExpiry_ReplaysFirstResponse`, `ExecuteAsync_SaveReturnsKeyConflict_ReturnsIdempotencyKeyInProgress`.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Payments\FakePaymentRepository.cs`: removed `AddAsync`; `Added` stays as the seed list.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\Infrastructure\RacingPaymentRepository.cs`: removed `AddAsync`.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\CreatePaymentTests.cs`: every test now uses `CreateClientWithNewKey()`, which sends a unique `Idempotency-Key`. Each test sends one request per client.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\DatabaseInitializerTests.cs`: added `InitializeAsync_IdempotencyTableMissing_CreatesTable`. `DisposeAsync` now runs `DatabaseInitializer.InitializeAsync` so the table is restored for the other tests.
