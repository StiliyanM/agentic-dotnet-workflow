VERDICT: CHANGES

The documentation diff mostly matches the code and the spec. One sentence about test coverage is wrong, and it appears in both files.

1. C:\Users\Stiliyan\source\repos\ApmPlayground\docs\user\provider-webhook.md:206 (check: accuracy)
   Problem: "The tests for copies that arrive at the same time cover two copies, not three or more." This is false. `Webhook_ConcurrentDuplicates_ProcessOnce` (tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs:619) sends 5 identical copies at the same time. Only the tests with different payloads use two copies: `Webhook_ConcurrentSameEventIdDifferentStatus_FirstSavedWinsAndLogsOneWarning` (:523) and `Webhook_ConcurrentSameEventIdOtherPayment_KeepsOtherPaymentPendingAndLogsOneWarning` (:559). The same-payload guard `Webhook_ConcurrentSameEventIdSamePayload_ProcessesOnceAndLogsNoWarning` (:594) also uses two.
   Change: limit the sentence to copies with different payloads. For example: "The tests for changed copies that arrive at the same time cover two copies, not three or more."

2. C:\Users\Stiliyan\source\repos\ApmPlayground\README.md:185 (check: accuracy)
   Problem: "The tests cover two copies at the same time, not three or more." The sentence comes after the case with different payloads, but on its own it says that no test covers more than two concurrent copies. That is wrong, because of the 5-copy test `Webhook_ConcurrentDuplicates_ProcessOnce`.
   Change: say "two copies with different payloads" (for example: "The tests cover two copies with different payloads at the same time, not three or more.").

What matches (no change needed):
- Required behavior: the request whose save fails reads the stored event again and compares the payload hashes. See `ProcessProviderWebhookUseCase.ExecuteAsync`, src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs:53-56. When the save fails on the `xmin` check instead, the code loops at :57-59, and the next read finds the stored event (:31-35). A mismatch then gives `DuplicatePayloadMismatch`, and the endpoint logs the existing warning. The documented result is the same in both paths.
- Both copies get `200`. On a mismatch there is one warning, logged by the copy whose save failed (decision E1). With the same payload there is no warning. The copy whose save failed changes no payment and adds no event row, and in C5 the other payment stays `Pending`. The tests at ProviderWebhookTests.cs:523-616 assert all of this (exactly one mismatch warning, stored hash = R1 hash, `otherPaymentEventCount == 0`).
- The warning text in provider-webhook.md matches the contract in the spec.
- Every test name in the new "Source" list exists: the integration tests at :523, :559, :594 and the unit tests at ProcessProviderWebhookUseCaseTests.cs:153 and :178.
- Status: nothing planned is described as available. The pre-spec-003 no-hash limitation is still documented, so the headings "not always logged" and "No warning for some changed resends" are still true.
- Check claims: the diff has no statement that a check or run passed, so it needs no evidence links. The statements about which tests cover a case are coverage statements, not pass claims.
- Limits: the diff does not claim production readiness.

Optional
- README.md:185 / provider-webhook.md:206: with three or more changed copies, the code makes each losing copy log one warning. This is not tested, which the docs already say. You could add "each other copy logs one warning" to make the expected behavior clear without claiming it is tested.
