VERDICT: PASS

I found no correctness or rule problems. The tests for this spec cover every acceptance case, decision and concurrency case, and each test can fail for the defect it targets.

Coverage against the spec (all at a suitable level):
- **A1/A2:** unit `ExecuteAsync_PendingPayment_AppliesStatusAndRecordsEventWithHash` (ProcessProviderWebhookUseCaseTests.cs:24) and integration `Webhook_ValidSignedEvent_Returns200AndUpdatesStatus` (ProviderWebhookTests.cs:56). The integration test also checks the stored hash.
- **A3/A5/D1:** `Webhook_TerminalPaymentRejectedRequest_ReturnsSpec002ResponseAndChangesNothing` (ProviderWebhookTests.cs:337) and `TryChangeStatus_FromTerminal_ReturnsFalseAndKeepsStatus` (PaymentTests.cs:65, which includes `Pending` as the target).
- **A4:** `ExecuteAsync_KnownEventIdSamePayload_...` (unit) and `Webhook_SameEventSamePayload_Returns200AndLogsNoWarning` (ProviderWebhookTests.cs:134).
- **A6:**
  - `RecordAsync_EventIdAlreadyStored_...` (ProviderWebhookTests.cs:553) proves there is no status change without its event.
  - `RecordAsync_PaymentChangedSinceLoad_...` (ProviderWebhookTests.cs:592) proves there is no event without its status change, and it proves the concurrency token works.
- **B1/B2/D2/D3/D4/D6:**
  - The unit test `ExecuteAsync_TerminalPayment_ReturnsIgnoredKeepsStatusAndRecordsEvent` (ProcessProviderWebhookUseCaseTests.cs:49) proves the rule.
  - The integration test `Webhook_TerminalPaymentNewEvent_...` (ProviderWebhookTests.cs:82) adds a distinct risk: the new `Ignored` outcome must map to 200 with no body, and the ignored row must be saved.
- **B3:** `Webhook_IgnoredEventRepeated_Returns200AndChangesNothing` (ProviderWebhookTests.cs:108). If ignored events were not recorded, the event count would be 0 and the test would fail.
- **B4/D5:**
  - Unit: `ExecuteAsync_KnownEventIdDifferentPayload_...` (ProcessProviderWebhookUseCaseTests.cs:90).
  - Integration: `Webhook_SameEventIdDifferentPayload_...` (ProviderWebhookTests.cs:413). It checks the warning category, that the warning contains the eventId, and that the other payment stays `Pending`.
- **Rows from before this spec (no hash):**
  - Unit `ExecuteAsync_KnownEventIdWithoutHash_ReturnsDuplicate` (ProcessProviderWebhookUseCaseTests.cs:117).
  - Integration `Webhook_EventStoredWithoutPayloadHash_CountsAsDuplicate` (ProviderWebhookTests.cs:449). It fails both if a null hash is treated as a mismatch (a warning is logged) and if it is treated as a new event (the status changes).
- **Schema upgrade:** `DatabaseInitializerTests.cs:42` and `:68`.
- **C1:** `Webhook_ConcurrentTerminalEvents_KeepFirstSavedStatusAndIgnoreOther` (ProviderWebhookTests.cs:485) meets the spec's test requirement.
  - `RacingPaymentRepository` holds each request's first `FindAsync` until both requests have loaded the payment.
  - `RacingWebhookEventRepository` / `WebhookRaceCoordinator` make the first event save first.
  - Without the `xmin` token, the second save writes the other terminal status and the status assertion fails, so the test can fail.
  - The retry logic is unit-tested in `ExecuteAsync_RecordReturnsPaymentChanged_LoadsAgainAndIgnoresEvent` (ProcessProviderWebhookUseCaseTests.cs:153). `Assert.Same(reloaded, recorded.Payment)` catches a retry that does not load the payment again.
  - The "attempts used up" case is tested at :183.
- **C2:** `Webhook_ConcurrentDuplicates_ProcessOnce` (unchanged) and the unit test `ExecuteAsync_RecordReturnsDuplicateEvent_ReturnsDuplicate` (ProcessProviderWebhookUseCaseTests.cs:135).

Form rules:
- **xUnit and Assert.Multiple:** the tests use xUnit asserts and `Assert.Multiple` for their verification assertions.
- **AutoFixture:** test data comes from AutoFixture (ids, amounts, currency, method). Statuses and case labels are given as InlineData.
- **Unit tests:** the test project references only Domain and Application (tests/AgenticPayments.UnitTests/AgenticPayments.UnitTests.csproj), and fakes are used only at the repository ports.
- **Integration tests:** they use the shared collection's container and clean up their data in `DisposeAsync` (through the event and payment id lists, or through the cascade in DatabaseInitializerTests).
- **Private methods:** no test calls a private method.

## Optional

1. C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\ProviderWebhookTests.cs:388, `Webhook_DuplicateEventId_Returns200AndDoesNotChangeStatusAgain`. This spec 002 test sends the same eventId with a different status. Under D5 that is the B4 mismatch scenario, which the "other status" case of `Webhook_SameEventIdDifferentPayload_Returns200ChangesNothingAndLogsWarning` (line 413) already covers, plus the warning check. The old test adds nothing now. Consider removing it, or folding it into the B4 theory.
2. ProviderWebhookTests.cs:337-369, `Webhook_TerminalPaymentRejectedRequest_ReturnsSpec002ResponseAndChangesNothing`. Each case goes through two string switches and converts `Action[]` into `Action<JsonElement>[]`, which makes it hard to see what each case expects. Three small Facts, or one switch that returns a single assert delegate, would read more clearly.
3. Several tests have guard asserts in the arrange step, outside `Assert.Multiple`:
   - ProviderWebhookTests.cs:572-573, 603-606, 642
   - ProcessProviderWebhookUseCaseTests.cs in `ExecuteAsync_RecordReturnsPaymentChanged_LoadsAgainAndIgnoresEvent` (the `Assert.True(reloaded.TryChangeStatus(...))` guard)
   - `SeedPayment` / `SeedPaymentAsync`

   This matches the earlier pattern in this suite and is not a verification step. If you want strict consistency, a seeding helper that throws on a false result would remove these asserts.
4. C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Webhooks\WebhookPayloadHashTests.cs:13, `Compute_KnownPayload_ReturnsLowercaseSha256OfCanonicalString`. The test recomputes the hash with the same algorithm as the production code. That is defensible, because the format must stay stable for rows already stored, and the plan specifies it. A fixed known-answer value (a constant Guid and its literal hex hash) would make the test independent of the code it checks.
