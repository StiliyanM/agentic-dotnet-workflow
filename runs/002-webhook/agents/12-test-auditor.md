# 12-test-auditor (loop 2 re-audit): final report

VERDICT: PASS

This was a full re-audit of `.agent-input/002-webhook/tests.diff` (loop 2). I checked it against specs/002-webhook.md, the Tests section of docs/architecture.md, and the HTTP contract and decisions in plans/002-webhook.md.

**Earlier findings, all fixed** (integration items are in tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs):
1. `Webhook_InvalidStatus_Returns400WithStatusError`: the test reads the payment status and the event count first. They are now checked in the same Assert.Multiple as the validation-problem contract, through `AssertValidationProblemAsync(..., params Action[] stateAssertions)`.
2. `Webhook_EmptyEventId_Returns400WithEventIdError`: the Pending-status check is now in the same Assert.Multiple.
3. `Webhook_UnknownPayment_Returns404AndStoresNoEvent`: the event count is now in the same Assert.Multiple, through `AssertProblemAsync(..., params Action<JsonElement>[])`.
4. `Webhook_SignedMalformedJson_Returns400Problem`: the check that there is no `errors` member is now in the same Assert.Multiple.
5. `AssertUnauthorizedAndNothingChangedAsync`: it reads the database state before asserting. The 401 problem and the state are now checked in one Assert.Multiple. The status-code `Assert.True` guard with the response body message stays before it, as in CreatePaymentTests.
6. `Webhook_InvalidSignature_Returns401`, row "two header values": the row now sends `[validSignature, validSignature]`. Only the multi-value rule (plan D1) can make the API return 401, so the row proves that rule.
7. The missing non-GUID `paymentId` case is covered by the new theory `Webhook_InvalidPaymentId_Returns400WithPaymentIdError` (rows `"not-a-guid"` and `123`). It expects a 400 validation problem under `paymentId` and checks that no event is stored.
8. The note is also fixed. `ExecuteAsync_RecordRejected_ReturnsDuplicate` in tests/AgenticPayments.UnitTests/Webhooks/ProcessProviderWebhookUseCaseTests.cs now asserts `RecordAttempts == 1` (new counter on FakeWebhookEventRepository) instead of an assertion that could not fail. This proves the rejected-record path was taken.

**Full re-check, no new problems:**
- Levels:
  - Unit tests cover the validator rules, the use-case outcomes and their order (validate, then duplicate check, then lookup, then record), and `Payment.ChangeStatus`.
  - Integration tests cover the HMAC signature (integration-only by plan D2, because the check lives in Api), JSON binding and error contracts, persistence and atomicity (`TryRecordAsync_EventIdAlreadyStored_ReturnsFalseAndSavesNothing`), sequential and concurrent idempotency, and the startup check for the secret.
  - `Webhook_EmptyEventId` repeats a validator rule through HTTP, but it adds a distinct risk: it is the only test of the path from the use case's Invalid result to the HTTP validation problem.
- Project references: the unit test project references only Domain and Application. Unit tests use fakes only for the ports.
- Asserts: all tests use xUnit asserts. Tests with more than one independent assertion use Assert.Multiple. The remaining assertions outside Assert.Multiple are guards whose values later assertions depend on (Assert.Throws, Assert.Single, Assert.NotNull, the status-code guard).
- Test data: AutoFixture makes payments, GUIDs and event ids. The literal values are intentional boundary or contract values.
- Integration setup: WebApplicationFactory with the shared PostgreSQL container through `ApiCollection`. DisposeAsync deletes this test's events, then its payments.
- Spec coverage:
  - HMAC: missing header, wrong secret, body changed after signing, no prefix, not hex, wrong length, two header values, uppercase hex, and signature checked before body parsing.
  - Idempotency: tested at the unit, repository and HTTP levels, sequential and concurrent.
  - Status update: both statuses, in any case.
  - Other cases: unknown payment, missing field, invalid status, invalid paymentId, malformed JSON, 415, and a missing secret at startup.
- No test mainly asserts on mocks, no test targets a private method, and every test can fail.
