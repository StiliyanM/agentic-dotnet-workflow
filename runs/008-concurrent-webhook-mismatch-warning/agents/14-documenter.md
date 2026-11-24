I updated the "Changed resends are not always logged" limitation in both files. It now matches the code and the passing tests for C3 to C6. I added no Status row for spec 008. Nothing outside `README.md` and `docs/user/` changed.

## Changed files

**1. `C:\Users\Stiliyan\source\repos\ApmPlayground\README.md`** ("Known limitations", "Changed resends are not always logged")

New or changed statements and their support:
- "Two copies with different payloads can arrive at the same time. Then both copies get `200`, only the first saved copy changes a payment, and the other copy logs one warning."
  - Code: `src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs`, the `DuplicateEvent` case (lines 53-56). It reads the stored event again and calls `ToDuplicateResult`.
  - Tests: `Webhook_ConcurrentSameEventIdDifferentStatus_FirstSavedWinsAndLogsOneWarning` (C3, C4) and `Webhook_ConcurrentSameEventIdOtherPayment_KeepsOtherPaymentPendingAndLogsOneWarning` (C5) in `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs`. They check `MismatchWarningCount == 1`.
  - Run: `runs/008-concurrent-webhook-mismatch-warning/evidence.md`, step 12-verify, exit 0. `verify/12-verify/summary.txt` shows integration exit=0.
- "The tests cover two copies at the same time, not three or more."
  - Source: `PostConcurrentlyAsync(client, firstBody, otherBody)` in those tests. Each test sends two requests.
- Kept: "There is no warning for events recorded before spec 003." Support: `ToDuplicateResult` in `ProcessProviderWebhookUseCase.cs`, where a null `PayloadHash` gives `Duplicate`.

**2. `C:\Users\Stiliyan\source\repos\ApmPlayground\docs\user\provider-webhook.md`**

- "Duplicate events" section: I added a bullet with three sub-bullets about two copies of one `eventId` that arrive at the same time.
  - "The first copy that is saved wins. The save of the other copy fails. The API then reads the stored event and compares the payload hashes." Support: `ProcessProviderWebhookUseCase.cs` lines 53-56 and `WebhookEventRepository.RecordAsync`, which maps a unique-key failure to `DuplicateEvent`.
  - "Different `paymentId` or `status`: `200`, no change, and one warning with the text above. The copy whose save failed logs it." Support: the C3, C4 and C5 integration tests above. The unit test `ExecuteAsync_RecordReturnsDuplicateEventStoredPayloadDiffers_ReturnsDuplicatePayloadMismatch` in `tests/AgenticPayments.UnitTests/Webhooks/ProcessProviderWebhookUseCaseTests.cs` covers both the other status and the other paymentId.
  - "Same `paymentId` and `status`: `200`, no change, no warning." Support: `Webhook_ConcurrentSameEventIdSamePayload_ProcessesOnceAndLogsNoWarning` (C6) and the unit test `ExecuteAsync_RecordReturnsDuplicateEventStoredPayloadSame_ReturnsDuplicate`.
  - "The copy whose save failed changes no payment and adds no event row. This is also true when it has another `paymentId`: that payment stays `Pending`." Support: the C5 test checks that the other payment is `Pending` and that `otherPaymentEventCount == 0`. The C3 and C4 tests check that one event row has the first hash (`FindSingleEventAsync`).
- "Duplicate events" source line: I added the three new integration tests and the two unit tests listed above.
- "Limits", "No warning for some changed resends":
  - Kept: no warning when the stored event has no `PayloadHash`. Support: `ToDuplicateResult` in `ProcessProviderWebhookUseCase.cs`.
  - Added: "For changed copies of one `eventId` that arrive at the same time, the API logs the warning." Support: the C3, C4 and C5 tests.
  - Added: "The tests for copies that arrive at the same time cover two copies, not three or more." Support: the same tests send two requests each.

## Removed statements (no longer true)

- README.md: "For two copies with different payloads that arrive at the same time, a warning is not guaranteed: it can be missing, depending on which save fails first."
- README.md: "No test covers the warning in this case."
- provider-webhook.md, Limits: "When two copies of one `eventId` with different payloads arrive at the same time, a warning is not guaranteed. It can be missing. This depends on which save fails first."
- provider-webhook.md, Limits: "No test covers the warning in this case."
- I did not repeat "Both copies get `200`, and only one copy changes the payment" in the Limits bullet of provider-webhook.md. It is still true, and the new bullets under "Duplicate events" now say it.

## Not changed
- **No Status row for spec 008 in the README table.** The spec is not merged yet. Under documenter rule 2 it is planned, and you did not ask for a row. Add the row as "Completed" after the merge if you want one.
- **No-warning fallback not documented.** If the stored event is not found after the unique-key failure, the code returns `Duplicate` with no warning. The unit test `ExecuteAsync_RecordReturnsDuplicateEvent_ReturnsDuplicate` covers this, and the code comment says it must not happen in production.
