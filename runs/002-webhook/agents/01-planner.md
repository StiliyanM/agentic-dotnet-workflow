# 01-planner: final report

I wrote the plan to C:\Users\Stiliyan\source\repos\ApmPlayground\plans\002-webhook.md. I did not write or change any other file.

**Main decisions** (the plan's Decisions section has D1 to D11):
- **Signature:** the header is `X-Provider-Signature` and its value is `sha256=` followed by the hex of an HMAC-SHA256 over the raw request body bytes. The key is the UTF-8 bytes of the configured secret. Hex can be in any case, and the comparison is constant-time (`CryptographicOperations.FixedTimeEquals`). The check lives in Api as the static class `ProviderWebhookSignature`. It turns on buffering, reads the body, rewinds it and checks the header. All of this happens before `JsonRequestBody.ReadAsync`.
- **Responses and check order:** the signature is checked first and any failure gives 401 problem. The body then goes through the existing `JsonRequestBody` contract: 415 for a non-JSON content type, 400 for a malformed body, a missing field or an invalid status. Validator errors give 400 under the keys `eventId`, `paymentId` and `status`. An unknown payment gives 404 problem. A processed event and a duplicate both give 200 with no body.
- **Status model:** `PaymentStatus` gets `Succeeded` and `Failed`. The webhook uses its own Application enum, `ProviderPaymentStatus { Succeeded, Failed }`, so `pending` is rejected with 400. `Payment.ChangeStatus(PaymentStatus)` has no transition rules yet. Spec 003 adds its rules inside `Payment.ChangeStatus` and handles event ordering. Until then any change is allowed, including Failed to Succeeded, and the last write wins.
- **Idempotency:** a new table `ProcessedWebhookEvents` has `EventId` (varchar(200)) as its primary key and a foreign key to `Payments` with cascade delete. The use case runs in this order: validate, then `ExistsAsync`, then `FindAsync` for the payment, then `ChangeStatus`, then `TryRecordAsync`. `TryRecordAsync` saves the event row and the payment in one `SaveChangesAsync`, which is one transaction. On a unique violation (Postgres code 23505) it returns false and the use case returns Duplicate (200).
- **Secret:** the config key is `Webhooks:Provider:Secret` and no committed file contains a value. In Development it comes from the environment variable `Webhooks__Provider__Secret`. In tests, `ApiFactory.UseSetting` sets a test-only constant `ApiFactory.WebhookSecret`. If the secret is empty, the app fails at start (`ValidateOnStart`).
- **Schema:** the app keeps `EnsureCreated`. The integration tests use a fresh container, so the new table is created there. A local development database that already has the `Payments` table must be dropped once, because `EnsureCreated` does nothing when tables exist. Migrations are left until a spec needs to keep data.

**Signatures:**
- Domain: `Payment.ChangeStatus`, `ProcessedWebhookEvent(string eventId, Guid paymentId)` with `MaxEventIdLength = 200`.
- Application: `IPaymentRepository.FindAsync(Guid, CancellationToken)`, `IWebhookEventRepository.ExistsAsync` and `IWebhookEventRepository.TryRecordAsync(ProcessedWebhookEvent, Payment, CancellationToken)`, `ProviderWebhookRequest`, `ProviderWebhookValidator`, `ProviderWebhookResult`, `ProviderWebhookOutcome`, `ProcessProviderWebhookUseCase.ExecuteAsync`.
- Api: `ProviderWebhookOptions`, `ProviderWebhookSignature.IsValidAsync(HttpRequest, string, CancellationToken)`, `WebhookEndpoints.MapWebhookEndpoints`.

**Tests:**
- **Unit tests** cover `ChangeStatus`, the validator and the use-case outcomes, using fakes. The test-writer must update `FakePaymentRepository` to implement `FindAsync` and add a new `FakeWebhookEventRepository`.
- **Integration tests** cover: every signature failure; that the signature is checked before the body is parsed; 415, 400 and 404; a duplicate eventId; parallel duplicate requests; a startup failure when the secret is missing.
- **Repository test:** the 23505 path and the single transaction are tested directly against `IWebhookEventRepository.TryRecordAsync`. The parallel HTTP test is not enough for this, because it does not reliably cause a race.
- **ApiFactory:** the test-writer must add the secret setting to `ApiFactory`.

**Pattern:** none.
