I fixed the two required accuracy items and applied the optional item. I made a small change to the optional item to keep it accurate: a losing copy whose payload matches the saved copy logs no warning. I changed only README.md and one file in docs/user/.

**Changed files**

1. C:\Users\Stiliyan\source\repos\ApmPlayground\docs\user\provider-webhook.md (line 206, Limits, "No warning for some changed resends")
   - Old: "The tests for copies that arrive at the same time cover two copies, not three or more."
   - New: "Each other copy with a payload that differs from the saved copy logs one warning. The tests for changed copies (different payloads) that arrive at the same time cover two copies, not three or more."

2. C:\Users\Stiliyan\source\repos\ApmPlayground\README.md (line 185, Known limitations, "Changed resends are not always logged")
   - Old: "The tests cover two copies at the same time, not three or more."
   - New: "With three or more changed copies, each other copy with a payload that differs from the saved copy logs one warning. The tests cover two copies with different payloads at the same time, not three or more."

**Support for each statement**

- **Each other copy logs one warning only when its payload differs from the saved copy.** Source: `src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs`, lines 53-56 and 70-73. On `WebhookRecordResult.DuplicateEvent`, each losing request reads the saved event again. It returns `DuplicatePayloadMismatch` only when the stored `PayloadHash` is not null and differs from its own hash. I do not claim that this is tested for three or more copies.
- **The tests with different payloads use two copies.** Sources in `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs`:
  - `Webhook_ConcurrentSameEventIdDifferentStatus_FirstSavedWinsAndLogsOneWarning`
  - `Webhook_ConcurrentSameEventIdOtherPayment_KeepsOtherPaymentPendingAndLogsOneWarning`
  - The same-payload guard `Webhook_ConcurrentSameEventIdSamePayload_ProcessesOnceAndLogsNoWarning` also uses two copies.
  - `Webhook_ConcurrentDuplicates_ProcessOnce` (line 628, `Enumerable.Range(0, 5)`) sends 5 identical copies. Neither new sentence denies this.

**Removed statements (no longer true)**
- docs/user/provider-webhook.md: "The tests for copies that arrive at the same time cover two copies, not three or more."
- README.md: "The tests cover two copies at the same time, not three or more."

The new text does not say that any check passed.
