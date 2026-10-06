I fixed all three required findings and two of the three optional ones. I kept the hash test and give the reason below. The tests now pass: unit 87/87, and 48/48 in the three affected integration test classes (`CreatePaymentIdempotencyTests`, `CreatePaymentTests`, `DatabaseInitializerTests`), run against a Testcontainers PostgreSQL. I did not run the full integration suite. I ran `dotnet format whitespace --folder tests`. I changed only files under `tests/`, wrote no production code and made no commit.

**Required findings**

1. Applied. In `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\CreatePaymentIdempotencyTests.cs`, `CreatePayment_SameKeyDifferentRequest_Returns422AndCreatesNothing` is now a `[Fact]` with only the A3 case. The second request has a different amount. The currency and method cases and the `difference` parameter are removed.

2. Applied. I deleted `CreatePayment_KeyLongerThan100_Returns400` from the same file.

3. Applied, with one correction to what the finding says. The tests did not leave idempotency records behind. The `IdempotencyRecords.PaymentId` foreign key has `ON DELETE CASCADE` (in `IdempotencyRecordConfiguration` and in the `DatabaseInitializer` DDL), so deleting the payments already deleted their records. I still added explicit deletion by key, so that cleanup does not depend on the foreign key and also covers a record whose payment id was never captured.
   - `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\CreatePaymentTests.cs`: new `_keys` list. `CreateClientWithNewKey` records each key. `DisposeAsync` deletes `IdempotencyRecords` with those keys first, then the payments.
   - `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\DatabaseInitializerTests.cs`: new `_keys` list. `InitializeAsync_IdempotencyTableMissing_CreatesTable` records its key. `DisposeAsync` deletes those records after the schema restore and before the payments.

**Optional findings**

- Hash test: not applied. I kept `CreatePaymentRequestHashTests.Compute_KnownRequest_ReturnsLowercaseSha256OfCanonicalString`. The plan lists this test and defines the canonical form (F2 amount, `"10.50|Eur|Ideal"`). The hash is stored in the database, so its format is a data contract. If the canonical text changes in a deploy, a valid retry of a key stored before the deploy gets 422 instead of the replay. A length/hex check would not catch that.
- Constructor test: applied. I removed `IdempotencyRecordTests.Constructor_SetsAllValues` from `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Payments\IdempotencyRecordTests.cs`. The integration tests that read the stored record back already check these values. The plan listed this test.
- Concurrency test: applied. `CreatePayment_ConcurrentSameKey_CreatesOnePayment` now requires exactly one 201 and exactly one 409, and checks the 409 problem contract on that single conflict response. The test can no longer pass without checking a 409.
  - Why this is safe: plan step 6 says `KeyConflict` returns 409 with no retry. The coordinator makes both requests look up the key before either saves, so the second save always gets `KeyConflict`.
  - Trade-off: the spec (C1) also allows two 201s with the same `paymentId`. An implementation that retries after a conflict would meet the spec but fail this test. The test now follows the plan, not only the spec.

**Test files and tests changed**
- `CreatePaymentIdempotencyTests.cs`: `CreatePayment_SameKeyDifferentRequest_Returns422AndCreatesNothing` (Theory to Fact), `CreatePayment_KeyLongerThan100_Returns400` (deleted), `CreatePayment_ConcurrentSameKey_CreatesOnePayment` (assertions tightened).
- `CreatePaymentTests.cs`: `DisposeAsync`, `CreateClientWithNewKey`.
- `DatabaseInitializerTests.cs`: `DisposeAsync`, `InitializeAsync_IdempotencyTableMissing_CreatesTable`.
- `IdempotencyRecordTests.cs`: `Constructor_SetsAllValues` (deleted).

PLAN GAP: none. PLAN UPDATE NEEDED: none.
