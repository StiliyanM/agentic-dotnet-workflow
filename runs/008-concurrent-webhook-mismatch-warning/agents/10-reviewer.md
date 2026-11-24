VERDICT: CHANGES

1. C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\Infrastructure\RacingWebhookEventRepository.cs:14 (`RecordAsync`). Check: style.
   - Problem: this diff removed the `ArgumentNullException.ThrowIfNull(webhookEvent)` guard, which moved to `WebhookRaceCoordinator.RecordInOrderAsync`. The method body is now a single `return coordinator.RecordInOrderAsync(...)` statement inside a block. `docs/csharp-style.md` ("Expression-bodied members for members that are one expression", a Reviewer rule) requires an expression body here. `FindAsync` in the same file already uses one.
   - Change: write it as `public Task<WebhookRecordResult> RecordAsync(...) => coordinator.RecordInOrderAsync(webhookEvent, () => inner.RecordAsync(webhookEvent, payment, cancellationToken), cancellationToken);`. This file is in `tests/`, so the test-writer owns it.

All other checks found no problems.

Correctness, by requirement:
- **Req 1:** in C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Application\Webhooks\ProcessProviderWebhookUseCase.cs:53-56, a `DuplicateEvent` result now reads the stored event again and compares its hash through `ToDuplicateResult` (lines 70-73).
  - The read sees the winner's row. In PostgreSQL, the unique-violation error comes only after the other transaction has committed. In the tests, the coordinator also lets R2 save only after R1's save has returned.
  - The `PaymentChanged` path (C3 and C4 when the `xmin` check fails first) still retries. On the retry, the first `FindAsync` finds the stored event and compares the hash. Both failure orders give the same result.
- **Req 2 and 3:** a different hash gives `DuplicatePayloadMismatch`, so the endpoint returns 200 and logs the existing warning. The same hash gives `Duplicate`: 200 and no warning.
- **Req 4:** the losing save fails as one unit and the change tracker is cleared (`WebhookEventRepository.cs:41-46`). Nothing is saved for the losing request.
- **Req 5:** no new exception path. If the second read finds no row, the result falls back to `Duplicate`, so there is no 500.

Idempotency: the losing request adds no event row and changes no payment. C5 asserts that Q stays `Pending` and has 0 event rows. C6 asserts exactly one event row.

Error handling: the signature check, the validation and the error responses did not change.

Code rules: `ToDuplicateResult` keeps the hash compare in one place (DRY). The pre-spec-003 rule (no hash counts as a duplicate) is kept. The change is small and adds no new types, schema changes or patterns (KISS, YAGNI).

Architecture: the change stays in the Application use case. No project references changed.

Spec decisions:
- **E1:** only R2, the request that lost, can get `DuplicatePayloadMismatch` and log the warning. R1 returns Processed. The tests assert exactly one mismatch warning, and also exactly one webhook warning of any text for E.
- **E3:** respected.
- **Contract that does not change:** the warning text, level and category are unchanged (`WebhookEndpoints.cs:56-59`). The tests match the exact text. A duplicate still gives 200 with no body. Decisions D1-D6 of spec 003 are not affected. No schema change.

Verification requirements:
- **Forced overlap:** C3-C6 force the overlap at the persistence boundary. The coordinator now selects R1 with a predicate on `ProcessedWebhookEvent`:
  - C3, C4 and C5 select R1 by its payload hash.
  - C6 uses `_ => true` with an atomic claim (`Interlocked.CompareExchange`), so only one request saves first.
- **Exactly one warning:** each test counts the warnings; none only checks that a warning exists.
- **Spec 003:** the existing race test now uses the shared `WithRace` helper. Its single-warning assertion is now stricter: it checks the exact text.

Optional:
- C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Webhooks\ProcessProviderWebhookUseCaseTests.cs:150 (`ExecuteAsync_RecordReturnsDuplicateEventStoredPayloadDiffers_ReturnsDuplicatePayloadMismatch`): the theory switches on string labels ("other status", "other paymentId") to build the stored event. Two separate `[Fact]` tests, or a `MemberData` of events, would avoid the `ArgumentOutOfRangeException` default branch.
- C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\ProviderWebhookTests.cs:494 (spec 003 race theory): `WithRace(coordinator, new CapturingLoggerProvider())` passes a logger provider that is never read. You could add an overload of `WithRace` without logs, or keep it as it is.
