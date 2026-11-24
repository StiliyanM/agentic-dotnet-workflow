The tests for spec 008 are written, and build and format pass against the current code. I ran the new tests before the fix: C5 fails, as required. I only changed files under `tests/` and did not commit.

## Plan check
I listed the spec's cases (C3, C4, C5 and C6 with forced overlap, exactly one warning, sequential A4/B4 still passing) before reading the plan. The plan has a matching test for each one. No PLAN GAP, no PLAN UPDATE NEEDED, no DECISION CONFLICT.

## Files changed
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\Infrastructure\WebhookRaceCoordinator.cs`
  - The constructor now matches the plan: `WebhookRaceCoordinator(Func<ProcessedWebhookEvent, bool> isFirst)`.
  - `RecordInOrderAsync` now takes `(ProcessedWebhookEvent webhookEvent, Func<Task<WebhookRecordResult>> record, CancellationToken cancellationToken)`.
  - The first event that matches `isFirst` claims the first slot with `Interlocked.CompareExchange` and saves at once. Every other save waits until that save has returned. `PaymentLoadedAsync` is unchanged.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\Infrastructure\RacingWebhookEventRepository.cs`: passes the whole `ProcessedWebhookEvent` to the coordinator. The coordinator now does the null check.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\ProviderWebhookTests.cs`
  - Added: `Webhook_ConcurrentSameEventIdDifferentStatus_FirstSavedWinsAndLogsOneWarning` (theory for C3 and C4). R1 is the request whose payload hash matches.
  - Added: `Webhook_ConcurrentSameEventIdOtherPayment_KeepsOtherPaymentPendingAndLogsOneWarning` (C5). R1 is selected by payload hash. It also checks that no event row exists for Q.
  - Added: `Webhook_ConcurrentSameEventIdSamePayload_ProcessesOnceAndLogsNoWarning` (C6). It uses the `_ => true` predicate, so the first request to reach the save is R1.
  - Changed: `Webhook_ConcurrentTerminalEvents_KeepFirstSavedStatusAndIgnoreOther` now uses `new WebhookRaceCoordinator(e => e.EventId == firstEventId)` and the new `WithRace` helper. Its assertions are unchanged.
  - New private helpers:
    - `WithRace(coordinator, logs)`: racing repositories plus the capturing logger on one factory.
    - `PostConcurrentlyAsync`: sends both requests with `Task.WhenAll` and reads the status and body before disposing the responses.
    - `MismatchWarningCount(logs, eventId)`: counts `Warning` entries in the `WebhookEndpoints` category that contain E. The tests assert the exact count, 1 or 0, not just that a warning exists.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Webhooks\ProcessProviderWebhookUseCaseTests.cs`
  - Added: `ExecuteAsync_RecordReturnsDuplicateEventStoredPayloadDiffers_ReturnsDuplicatePayloadMismatch` (theory: "other status", "other paymentId").
  - Added: `ExecuteAsync_RecordReturnsDuplicateEventStoredPayloadSame_ReturnsDuplicate`.
  - New private helper `LoseRaceTo(winnerEvent)`: it queues `DuplicateEvent` and uses `BeforeResultReturned` to add the winner's event to `Stored`. So the first read finds nothing, and only a read after the failed save finds the winner.
  - `ExecuteAsync_RecordReturnsDuplicateEvent_ReturnsDuplicate` is unchanged.

## Checks run
- `dotnet format whitespace --folder tests`: done.
- `bash scripts/verify.sh build format`, with the results in the scratchpad: build passed, format passed, exit 0.

## Results before the fix (my own run, for information)
The orchestrator's before-fix step is still the official record.
- **Unit:** both cases of the new `...StoredPayloadDiffers...` theory fail: the current code returns `Duplicate` instead of `DuplicatePayloadMismatch`. All other tests in the class pass (17 of 19), including the new same-payload test.
- **C5:** fails. Mismatch warning count expected 1, actual 0. All its other assertions pass: both 200 with empty body, P is `Succeeded`, Q is `Pending`, one event row for E linked to P with R1's hash.
- **C3 and C4:** both failed in this run, for the same reason only (warning count 0). Under E3 they may pass or fail, so the orchestrator should record whatever its run shows.
- **C6 and the existing tests:** C6 passed, as did `Webhook_ConcurrentTerminalEvents_*`, `Webhook_SameEventIdDifferentPayload_*`, `Webhook_SameEventSamePayload_*` and `Webhook_EventStoredWithoutPayloadHash_CountsAsDuplicate`.
- **Timing:** each race test finished in a few seconds. None waited until the 30-second limit in the coordinator.
