# 08-test-auditor: final report

VERDICT: FAIL

Scope: `.agent-input/002-webhook/tests.diff`, checked against specs/002-webhook.md, the Tests section of docs/architecture.md, and the test contract and decisions in plans/002-webhook.md (D1 to D11). The unit test project references only Domain and Application, which is correct. AutoFixture builds the test data. Each integration test cleans up its own events and payments in DisposeAsync, and events are deleted before payments. Unit tests use fakes only for the ports.

## Problems (blocking)

All integration items are in `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs`.

1. **`Webhook_InvalidStatus_Returns400WithStatusError`**
   - Rule: use Assert.Multiple when a test has more than one assertion.
   - Problem: `Assert.Equal(PaymentStatus.Pending, await ReadPaymentStatusAsync(paymentId))` runs on its own after `AssertValidationProblemAsync`. If the HTTP assertions fail, the database state is never reported.
   - Fix: read the payment status first. Then assert the response contract and the unchanged status in one Assert.Multiple. For example, add a variant of `AssertValidationProblemAsync` that accepts extra assertions, or make it return the parsed values.
2. **`Webhook_EmptyEventId_Returns400WithEventIdError`**
   - Rule: Assert.Multiple, same problem as item 1.
   - Problem: the Pending-status assert is separate from the validation-problem asserts.
   - Fix: same as item 1.
3. **`Webhook_UnknownPayment_Returns404AndStoresNoEvent`**
   - Rule: Assert.Multiple.
   - Problem: `Assert.Equal(0, await CountEventsAsync(eventId))` is separate from the 404 problem assertions.
   - Fix: read the count first, then assert status, content type, problem status and event count in one Assert.Multiple.
4. **`Webhook_SignedMalformedJson_Returns400Problem`**
   - Rule: Assert.Multiple.
   - Problem: `Assert.False(problem.RootElement.TryGetProperty("errors", out _))` is separate from the Multiple block in `AssertProblemAsync`. It does not depend on those assertions.
   - Fix: put the no-`errors` check in the same Assert.Multiple. The existing `AssertPlainProblemAsync` in CreatePaymentTests.cs is a pattern you can copy.
5. **`AssertUnauthorizedAndNothingChangedAsync`**
   - Affects `Webhook_MissingSignature_Returns401AndChangesNothing` and every row of `Webhook_InvalidSignature_Returns401`.
   - Rule: Assert.Multiple.
   - Problem: the helper has two separate Assert.Multiple blocks: the problem response from `AssertProblemAsync`, then the DB state. If the response block fails, the DB block never runs.
   - Fix: read the payment status and event count first, then assert the 401 problem contract and the DB state in one Assert.Multiple. Keeping the status-code `Assert.True` guard with the response body message, as the existing tests do, is fine.
6. **`Webhook_InvalidSignature_Returns401`, row `"two header values"`**
   - Rule: tests must be able to fail. The edge case "a header with more than one value is invalid" (plan D1, HTTP check step 1) must be proven.
   - Problem: the row sends `[validSignature, Sign(body, OtherSecret)]`. An implementation that ignores the multi-value rule and checks only the last value still returns 401, so this row cannot catch that bug.
   - Fix: send two copies of the valid signature, `[validSignature, validSignature]`. Then only the multi-value rule can produce 401.
7. **Missing edge case: non-GUID `paymentId`**
   - Rule: edge cases from the spec contract need a test. Per the plan's HTTP contract and architecture.md Contracts, a value the reader cannot convert gives a 400 validation problem under the field key.
   - Problem: no test sends, for example, `"paymentId":"not-a-guid"` or `"paymentId":123`. GUID conversion is a different converter path from the enum status rows, and only HTTP can prove it.
   - Fix: add a theory, for example `Webhook_InvalidPaymentId_Returns400WithPaymentIdError` with rows `"\"not-a-guid\""` and `"123"`. Expect `paymentId` / `"PaymentId has an invalid value."`, and check that no event is stored.

## Notes (not blocking)

- `tests/AgenticPayments.UnitTests/Webhooks/ProcessProviderWebhookUseCaseTests.cs`, `ExecuteAsync_RecordRejected_ReturnsDuplicate`: `Assert.Empty(_webhookEvents.Recorded)` cannot fail, because the fake never records when `RejectRecord` is true. The test is still valid through the `Outcome` assertion. Remove the dead assertion, or replace it with an assertion that `TryRecordAsync` was attempted (for example a counter on the fake).
- Level choices are sound:
  - Signature checks are integration-only, as plan D2 records (it is an HTTP concern in Api, and unit tests cannot reference Api).
  - `Webhook_EmptyEventId` is the only test of the path from validator errors to the HTTP 400, so it adds a distinct risk beyond `ProviderWebhookValidatorTests.Validate_EventIdEmpty_ReturnsEventIdError`.
  - `TryRecordAsync_EventIdAlreadyStored_ReturnsFalseAndSavesNothing` is a deterministic persistence test of the unique-key path and the one-transaction save.
- Spec coverage otherwise:
  - HMAC: missing, wrong secret, changed body, no prefix, not hex, wrong length, uppercase hex, and signature checked before parsing are all tested.
  - Idempotency: sequential and concurrent duplicates are tested at the unit, repository and HTTP levels.
  - Status update: both statuses are tested at the unit and HTTP levels.
  - Unknown payment, missing fields, invalid status, 415, malformed JSON and a missing secret at startup are tested.
