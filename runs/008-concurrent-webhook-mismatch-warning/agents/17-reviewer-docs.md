VERDICT: APPROVE

I checked the documentation diff for spec 008 (`README.md` "Known limitations" and `docs/user/provider-webhook.md`) against the spec, the current code and the tests. I found nothing that must change.

- **Accuracy:**
  - The statements below match `src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs` (DuplicateEvent case, lines 53-56: a new read with `FindAsync`, then `ToDuplicateResult`) and `src/AgenticPayments.Infrastructure/Persistence/WebhookEventRepository.cs` (one `SaveChanges`; on a unique-key failure it clears the change tracker):
    - First saved copy wins.
    - The losing copy's save fails, then the API reads the stored event and compares hashes.
    - Different payload: `200`, no change, one warning, logged by the losing copy.
    - Same payload: `200`, no change, no warning.
    - The losing copy changes no payment and adds no event row, and the other payment stays `Pending`.
  - The doc uses the existing warning text ("with the text above") and does not change it.
  - All test names in the doc exist:
    - Integration tests in `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs` at lines 523, 559, 594, 619 and 644.
    - Unit tests in `tests/AgenticPayments.UnitTests/Webhooks/ProcessProviderWebhookUseCaseTests.cs` at lines 153 and 178.
  - These tests assert what the doc says: exactly one mismatch warning, the other payment stays `Pending`, `0` event rows for the other payment, and `0` warnings when the payload is the same.
  - The old text about a missing warning ("not guaranteed", "can be missing", "No test covers the warning") is gone from `README.md` and `docs/user/`.
- **Status:** No planned behavior is described as available. For three or more changed copies, the docs describe what the code does and say clearly that the tests cover only two copies.
- **Check claims:** The diff says nowhere that a check or test run passed. It only names tests as sources. So there is no statement without a link to the run evidence to list.
- **Limits:** The diff does not claim production readiness. Both files keep the change in the limitations sections.

## Optional
- In `README.md` "Known limitations", "Changed resends are not always logged": this heading no longer fits well, because concurrent changed copies are now logged. Only events recorded before spec 003 stay without a warning. A heading such as "Changed resends of old events are not logged" would be more exact. The same applies to "No warning for some changed resends" in `docs/user/provider-webhook.md`, which is less of a problem.
- In `README.md`: the sentence "only the first saved copy changes a payment" is correct, but a copy that arrives after the payment is already final changes nothing. "At most the first saved copy changes a payment" would be more exact.
- Outside this diff: `README.md` "Two requests with the same new key at the same time" has an older typo, "must retry, When". This spec did not add it.

Files checked:
- C:\Users\Stiliyan\source\repos\ApmPlayground\README.md
- C:\Users\Stiliyan\source\repos\ApmPlayground\docs\user\provider-webhook.md
- C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Application\Webhooks\ProcessProviderWebhookUseCase.cs
- C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Infrastructure\Persistence\WebhookEventRepository.cs
- C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\ProviderWebhookTests.cs
- C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Webhooks\ProcessProviderWebhookUseCaseTests.cs
