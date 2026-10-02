VERDICT: FAIL

The fail comes from 2 rule findings. Both are about form. Coverage and the ability to fail are good: every spec case (A1–A6, B1–B4, C1, C2, the pre-spec rows with no hash, and the schema upgrade) has a test that can fail.

## Findings

1. **rule**: `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\ProviderWebhookTests.cs`, `RecordAsync_PaymentNotTracked_ThrowsInvalidOperationException`, lines 648-658.
   - Rule broken: "Use Assert.Multiple when a test has more than one assertion."
   - The outcome assertion `await Assert.ThrowsAsync<InvalidOperationException>(...)` (line 648) is outside the `Assert.Multiple` that checks the state (lines 656-658). If the guard is missing, the test stops at `ThrowsAsync`, and the "nothing saved" checks never run or report. Reporting all of them together is the reason for the rule.
   - Fix: use `var exception = await Record.ExceptionAsync(() => webhookEvents.RecordAsync(...));`. Then put `() => Assert.IsType<InvalidOperationException>(exception)` in the same `Assert.Multiple` as the status and event-count assertions.

2. **rule**: `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Webhooks\ProcessProviderWebhookUseCaseTests.cs`, `ExecuteAsync_RecordReturnsPaymentChangedEveryTime_ThrowsInvalidOperationException`, lines 189-193.
   - Rule broken: "Use Assert.Multiple when a test has more than one assertion."
   - `Assert.ThrowsAsync` (line 189) is outside the `Assert.Multiple` that checks `RecordAttempts` and `Recorded` (lines 191-193).
   - Fix: same as finding 1. Capture the exception with `Record.ExceptionAsync`, then assert `Assert.IsType<InvalidOperationException>(exception)` together with the attempt count and the empty `Recorded` in one `Assert.Multiple`.

## Checked and accepted (no finding)

- **C1, `Webhook_ConcurrentTerminalEvents_KeepFirstSavedStatusAndIgnoreOther`** (ProviderWebhookTests.cs:485)
  - `WebhookRaceCoordinator` makes both requests load the `Pending` payment before either one saves, and it fixes which one saves first. So the test does more than `Task.WhenAll`, as the spec requires.
  - It can fail: without the `xmin` token the second save writes over the first terminal status.
  - If the two requests do not overlap, the 30 s wait limit makes the test fail, so it cannot pass without the overlap.
  - The "inside their own transaction" wording is covered by a FLAGGED plan decision.
- **A6**
  - `RecordAsync_EventIdAlreadyStored_...` (line 553) covers "no status change without its event".
  - `RecordAsync_PaymentChangedSinceLoad_...` (line 592) covers "no event without its status change".
  - Both are real persistence checks.
- **A3 and A5, `Webhook_TerminalPaymentRejectedRequest_...`** (line 337): the state assertions run inside the helper's `Assert.Multiple` (`AssertProblemAsync`, lines 791-809).
- **B1–B4 and the no-hash row: HTTP tests vs. unit tests.** Each of these HTTP tests adds a risk that its unit test cannot cover:
  - the endpoint mapping of `Ignored` and `DuplicatePayloadMismatch` to 200 with no body;
  - that the ignored event is really saved;
  - that the stored hash comes back unchanged from the database;
  - that a NULL `PayloadHash` is read back correctly;
  - the warning log, which is written in Api, and unit tests cannot reference Api.
- **Project references:** unit tests reference only Domain and Application.
- **Test data:** comes from AutoFixture (ids, amounts, currency, method). Status values in `InlineData` are scenario parameters.
- **`DatabaseInitializerTests`:** it puts the column back in `DisposeAsync` and deletes its own payment rows. It is in the shared collection, so its tests run one after the other with the other tests there. `InitializeAsync_RunTwice_DoesNotFail` can fail: without `IF NOT EXISTS`, adding a column that already exists throws.
- **Mocks:** no test mainly checks mock calls. `FindCalls` and `RecordAttempts` sit next to real outcome assertions.
- **No test of a private method.**

## Optional

- `ProcessProviderWebhookUseCaseTests.cs:176`, `ExecuteAsync_RecordReturnsPaymentChanged_LoadsAgainAndIgnoresEvent`: `Assert.Equal(PaymentStatus.Succeeded, reloaded.Status)` cannot fail. The test sets `reloaded` to a terminal status, and the domain rule stops any change. The other assertions in the test (`Ignored`, `Assert.Same(reloaded, ...)`, `FindCalls == 2`) already detect the defect. This line can be removed.
- `ProviderWebhookTests.cs:388`, `Webhook_DuplicateEventId_Returns200AndDoesNotChangeStatusAgain` (kept from spec 002): it now sends the same `eventId` with a different status. That makes it a B4 case, which the `"other status"` case of `Webhook_SameEventIdDifferentPayload_...` (line 413) already covers, including the warning. You could remove it or merge it.
- `ProviderWebhookTests.cs:82`, `Webhook_TerminalPaymentNewEvent_...`: the second `InlineData` (failed then succeeded) goes through the same HTTP mapping as the first. The unit theory at `ProcessProviderWebhookUseCaseTests.cs:49` already proves B2. One HTTP case would be enough.
- `ProviderWebhookTests.cs:337`, A3 theory: it seeds only `Succeeded`. A `Failed` row would cover the other terminal status, but the rule is the same.
- Guard assertions in the arrange step stay outside `Assert.Multiple`: `Assert.True(payment.TryChangeStatus(...))` and `Assert.NotNull(payment)`, for example at ProviderWebhookTests.cs:642 and in the `SeedPayment` helpers. These match the existing precedent (for example `AssertProblemAsync`, lines 797-799) and are fine as preconditions.
