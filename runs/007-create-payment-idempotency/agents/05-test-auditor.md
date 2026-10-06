VERDICT: FAIL

The tests cover every acceptance case in the spec (A1–A6 and C1). The C1 test meets the spec's test requirement. The coordinator makes both requests look up the key before either one saves, and it runs the two saves one after the other. Without a unique-key protection, both inserts would succeed and the test would see 2 payments, so `Assert.Equal(1, paymentCount)` would fail. The unit tests reference only Domain and Application. Assert.Multiple and AutoFixture are used correctly. The FAIL comes from three findings: two tests at the wrong level and one cleanup gap.

## Findings

1. **correctness (wrong level: repeats a unit-tested rule through HTTP)**
   - File: `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\CreatePaymentIdempotencyTests.cs`, lines 110-144
   - Test: `CreatePayment_SameKeyDifferentRequest_Returns422AndCreatesNothing`, cases `currency` and `method`
   - Rule: test each rule at the lowest level. Do not repeat every unit-test case through HTTP unless the HTTP path adds a distinct risk.
   - Problem: two unit tests already prove that a change in amount, currency or method is a different request:
     - `ExecuteAsync_SameKeyDifferentRequest_ReturnsIdempotencyKeyReusedAndSavesNothing` (theory: amount, currency, method) in `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Payments\CreatePaymentUseCaseTests.cs`
     - `Compute_DifferentAmountCurrencyOrMethod_ReturnsDifferentHash` in `CreatePaymentRequestHashTests.cs`

     The HTTP path adds only one distinct risk: the 422 problem contract and "no new payment / record unchanged" in persistence. One case is enough to prove that.
   - Fix: make it a `[Fact]` with only the A3 case (different amount). Remove the `currency` and `method` cases.

2. **correctness (wrong level: repeats a unit-tested rule through HTTP)**
   - File: `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\CreatePaymentIdempotencyTests.cs`, lines 165-182
   - Test: `CreatePayment_KeyLongerThan100_Returns400`
   - Rule: same as finding 1.
   - Problem: three tests already cover this:
     - `IdempotencyKeyValidatorTests.Validate_LongerThan100_ReturnsLengthError` proves the max-length rule and its message.
     - `CreatePaymentUseCaseTests.ExecuteAsync_InvalidKey_ReturnsInvalidAndDoesNotTouchRepository` (the 101-character case) proves that the use case rejects the key before it uses the repository.
     - `CreatePayment_MissingOrEmptyKey_Returns400WithKeyError` already proves that an invalid key maps to a 400 response with the `Idempotency-Key` error.

     The 101-character request adds no distinct HTTP or persistence risk. The persistence risk of the column length is already covered by `CreatePayment_KeyOf100Characters_Returns201`.
   - Fix: delete this integration test.

3. **rule (each test must clean its own data)**
   - Files and tests:
     - `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\CreatePaymentTests.cs`, `DisposeAsync` (lines 30-40). It affects every successful create in this class, because each request now sends a new key through `CreateClientWithNewKey`, so it stores an `IdempotencyRecords` row.
     - `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\DatabaseInitializerTests.cs`, `DisposeAsync` (lines 22-36), and `InitializeAsync_IdempotencyTableMissing_CreatesTable` (starts at line 82). This test inserts an `IdempotencyRecord` directly.
   - Problem: both `DisposeAsync` methods delete only `Payments`. They never delete the idempotency records that the tests created. The tests do not say that a cascade removes these rows, unlike the existing comment for the webhook events. `CreatePaymentIdempotencyTests` deletes its records explicitly, which suggests no cascade is assumed. If there is no cascade, these rows stay in the shared container.
   - Fix: record the keys that each test uses (for example, have `CreateClientWithNewKey` add the key to a list, and have the DatabaseInitializer test add its key). In `DisposeAsync`, delete `db.IdempotencyRecords.Where(r => keys.Contains(r.Key))` before the payments.

## Optional

- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Payments\CreatePaymentRequestHashTests.cs`, line 14, `Compute_KnownRequest_ReturnsLowercaseSha256OfCanonicalString`: the test fixes the exact internal canonical string `"10.50|Eur|Ideal"`. The spec does not require this format. The two other hash tests (`Compute_SameAmountOtherScale_ReturnsSameHash` and `Compute_DifferentAmountCurrencyOrMethod_ReturnsDifferentHash`) already prove the behaviour. You could keep only the length/hex-format check, or remove the test.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Payments\IdempotencyRecordTests.cs`, `Constructor_SetsAllValues`: this test only checks that the constructor assigns its values, and other tests (`Renew_*` and the use-case tests) already cover that. You could remove it.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\CreatePaymentIdempotencyTests.cs`, `CreatePayment_ConcurrentSameKey_CreatesOnePayment`: the 409 problem check uses `Assert.All` on a list that can be empty. With the current coordinator, the second save always gets KeyConflict, so 409 is effectively always returned. You could make the test say so (for example, assert that `conflicts` has exactly one entry), so that the 409 HTTP contract is always checked.
