I wrote the tests for spec 003-out-of-order in `tests/` only. Nothing is committed and nothing in `src/` was touched. The build fails as expected: both test projects stop on the missing `WebhookRecordResult` type and on the old `IWebhookEventRepository` members (`ExistsAsync` and `TryRecordAsync` are still there). Those were the only errors either build reported, though the compiler may show more once those types exist. `dotnet format whitespace --folder tests` ran with exit 0, and the `--verify-no-changes` check also exited 0.

There is one PLAN UPDATE NEEDED and no PLAN GAP.

## Changed and added files and tests

**`tests/AgenticPayments.UnitTests/Payments/PaymentTests.cs`** (changed)
- The `ChangeStatus_SetsStatus` test is removed.
- Added `TryChangeStatus_FromPending_SetsStatusAndReturnsTrue` (Theory: Succeeded, Failed).
- Added `TryChangeStatus_FromTerminal_ReturnsFalseAndKeepsStatus` (Theory: Succeeded or Failed, each followed by Pending, Succeeded or Failed).

**`tests/AgenticPayments.UnitTests/Payments/FakePaymentRepository.cs`** (changed)
- Added `Reloaded` (`Dictionary<Guid, Payment>`): `FindAsync` returns this payment instead of the seeded one. It simulates a fresh load after another request changed the payment.
- Added `FindCalls`.

**`tests/AgenticPayments.UnitTests/Webhooks/FakeWebhookEventRepository.cs`** (changed)
- Uses the new port members `FindAsync` and `RecordAsync`.
- `Stored` holds seeded events, with or without a hash. `Recorded` holds the events that were saved.
- `RecordResults` is a queue of results; when it is empty, `RecordAsync` returns `Recorded`.
- `BeforeResultReturned` is the hook the plan asks for. `RecordAttempts` counts the calls.

**`tests/AgenticPayments.UnitTests/Webhooks/WebhookPayloadHashTests.cs`** (new)
- `Compute_KnownPayload_ReturnsLowercaseSha256OfCanonicalString`
- `Compute_DifferentPaymentIdOrStatus_ReturnsDifferentHash` (Theory)

**`tests/AgenticPayments.UnitTests/Webhooks/ProcessProviderWebhookUseCaseTests.cs`** (changed)
- `ExecuteAsync_PendingPayment_AppliesStatusAndRecordsEventWithHash` (Theory) covers A1 and A2.
- `ExecuteAsync_TerminalPayment_ReturnsIgnoredKeepsStatusAndRecordsEvent` (Theory, 4 rows) covers B1, B2 and the same terminal status sent again.
- `ExecuteAsync_KnownEventIdSamePayload_ReturnsDuplicateAndChangesNothing` covers A4.
- `ExecuteAsync_KnownEventIdDifferentPayload_ReturnsDuplicatePayloadMismatchAndChangesNothing` (Theory: other status, other paymentId) covers B4.
- `ExecuteAsync_KnownEventIdWithoutHash_ReturnsDuplicate` (Theory: same and other status) covers stored events that have no hash.
- `ExecuteAsync_RecordReturnsDuplicateEvent_ReturnsDuplicate` replaces `ExecuteAsync_RecordRejected_ReturnsDuplicate`.
- `ExecuteAsync_RecordReturnsPaymentChanged_LoadsAgainAndIgnoresEvent` covers the C1 logic. It asserts `Ignored`, 2 record attempts, 2 payment loads, and that the reloaded payment instance is the one saved.
- `ExecuteAsync_RecordReturnsPaymentChangedEveryTime_ThrowsInvalidOperationException` asserts exactly `MaxAttempts` record attempts.
- `ExecuteAsync_UnknownPayment_ReturnsPaymentNotFoundAndRecordsNothing` and `ExecuteAsync_InvalidRequest_ReturnsInvalidWithErrorsAndRecordsNothing` are kept and adapted.

**`tests/AgenticPayments.IntegrationTests/Infrastructure/`**
- **`CapturingLoggerProvider.cs`** (new): keeps each log entry's category, level and message.
- **`WebhookRaceCoordinator.cs`** (new): makes both requests load the payment before either one saves, then lets the chosen event save first. The other event saves only after that save returns. A 30-second wait limit makes a broken implementation fail instead of hang.
- **`RacingPaymentRepository.cs`** (new): scoped decorator over the real `PaymentRepository`. The first `FindAsync` of each request waits at the coordinator.
- **`RacingWebhookEventRepository.cs`** (new): scoped decorator over the real `WebhookEventRepository`. The coordinator decides the order of `RecordAsync`.

**`tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs`** (changed)
- `Webhook_ValidSignedEvent_Returns200AndUpdatesStatus` now also asserts the stored `PayloadHash`. The `FAILED` row checks that the casing of the status does not change the hash.
- New:
  - `Webhook_TerminalPaymentNewEvent_Returns200KeepsStatusAndRecordsEvent` (Theory) covers B1 and B2.
  - `Webhook_IgnoredEventRepeated_Returns200AndChangesNothing` covers B3.
  - `Webhook_SameEventSamePayload_Returns200AndLogsNoWarning` covers A4.
  - `Webhook_TerminalPaymentRejectedRequest_ReturnsSpec002ResponseAndChangesNothing` (Theory: wrong signature 401, `pending` 400, empty eventId 400) covers A3 and A5 with D1. The payment is seeded as `Succeeded`, and the test asserts no event rows for the payment.
  - `Webhook_SameEventIdDifferentPayload_Returns200ChangesNothingAndLogsWarning` (Theory: other status, other paymentId; the second payment stays Pending) covers B4.
  - `Webhook_EventStoredWithoutPayloadHash_CountsAsDuplicate` sets `PayloadHash` to NULL with SQL and asserts 200, Pending, the hash still null, and no warning.
  - `Webhook_ConcurrentTerminalEvents_KeepFirstSavedStatusAndIgnoreOther` (Theory: succeeded first, failed first) covers C1. It registers the racing decorators with `ConfigureTestServices`. Without the `xmin` token, the second save overwrites the status and the test fails.
  - `RecordAsync_EventIdAlreadyStored_ReturnsDuplicateEventAndSavesNothing` replaces the old `TryRecordAsync_...` test. It also asserts that the change tracker is empty afterwards.
  - `RecordAsync_PaymentChangedSinceLoad_ReturnsPaymentChangedAndSavesNothing` covers A6 and the token. It also asserts that the change tracker is cleared.
  - `RecordAsync_PaymentNotTracked_ThrowsInvalidOperationException`
- Kept: all tests from spec 002, including `Webhook_DuplicateEventId_Returns200AndDoesNotChangeStatusAgain`, `Webhook_ConcurrentDuplicates_ProcessOnce` (C2) and `Startup_WebhookSecretMissing_Fails`.
- New helpers: `SeedPaymentAsync(status)`, `CountEventsForPaymentAsync`, `WithCapturedLogs`, `WebhookWarnings`, `SignedBody`.

**`tests/AgenticPayments.IntegrationTests/DatabaseInitializerTests.cs`** (new; same `ApiCollection`, so it runs serially)
- `InitializeAsync_PayloadHashColumnMissing_AddsNullableColumnAndKeepsRows` asserts the column is `is_nullable = YES` with length 64, and that the old row is kept with a NULL hash.
- `InitializeAsync_RunTwice_DoesNotFail`
- `DisposeAsync` adds the column back with `ADD COLUMN IF NOT EXISTS` if it is missing, so a broken initializer cannot break the other tests. It also deletes the seeded payment; the event rows go with it by cascade.

## Choices that differ from the plan's wording
- **`RecordAsync_PaymentNotTracked_...`:** the plan creates the payment with `new`. My test uses a stored payment that was loaded with `AsNoTracking` in another scope. That catches the real defect: without the guard, the event would be saved without its status change. A payment created with `new` would fail on the foreign key anyway, so the test could pass without the guard.
- **Change tracker checks:** the two `RecordAsync` result tests also assert that the change tracker is empty. The interface comment states this, and the use case's reload depends on it.

## PLAN UPDATE NEEDED
- **`ProcessedWebhookEvent(string eventId, Guid paymentId, string payloadHash)`:** the constructor parameter is non-nullable, but `PayloadHash` is `string?`. The plan's fake must be seedable with events without a hash, which needs a `ProcessedWebhookEvent` with a null hash. Unit test `ExecuteAsync_KnownEventIdWithoutHash_ReturnsDuplicate` builds one with `null!` and a comment that explains why. The plan must either state that the constructor accepts null without a guard (no `ThrowIfNull(payloadHash)`), or change the parameter to `string? payloadHash`. Otherwise this test throws.

## PLAN GAP
None. Each spec case (A1–A6, B1–B4, C1, C2, events stored without a hash) has a matching test in the plan. One mismatch is already FLAGGED in the plan: the spec's C1 requirement says each request reads the payment "inside their own transaction". With the plan's optimistic `xmin` design, each request reads the payment in its own scoped context and not in an explicit transaction. The C1 test follows the plan's shape: both requests read before either saves, and the first save comes first.
