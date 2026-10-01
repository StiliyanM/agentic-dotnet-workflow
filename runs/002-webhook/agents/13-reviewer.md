# 13-reviewer: final report

VERDICT: APPROVE

I found no required changes. I checked all three spec requirements against the code and found no problems.

**Correctness**
- HMAC-SHA256 signature check: `src/AgenticPayments.Api/Webhooks/ProviderWebhookSignature.cs` computes the HMAC over the raw body bytes. It compares the result in fixed time against the `X-Provider-Signature: sha256=<hex>` header. A missing header, more than one header value, a missing prefix, bad hex or the wrong length all fail the check. The body is buffered and rewound, so the JSON reader can read it afterwards.
- Secret: the secret comes from configuration (`Webhooks:Provider:Secret`). It is validated when the app starts (`src/AgenticPayments.Api/Program.cs:18-21`).
- Status update: the provider status (Succeeded or Failed) maps to `PaymentStatus` and is applied with `Payment.ChangeStatus`. The two new `PaymentStatus` members are stored as strings, as the existing payment statuses are.

**Idempotency**
- The use case checks `ExistsAsync` before it changes anything (`ProcessProviderWebhookUseCase.cs:221`).
- `EventId` is the primary key (`ProcessedWebhookEventConfiguration.cs:15`). If two copies of an event arrive at the same time, the second one fails with a unique violation. `WebhookEventRepository.TryRecordAsync` turns that into `false`, and the endpoint returns 200 (Duplicate).
- The event row and the payment status change are saved in one `SaveChanges`, so either both are stored or neither is.

**Error handling**
- Every failure leaves no event row and no status change: 401 problem for a bad signature (checked before the body is parsed); 415 for a content type that is not JSON; 400 problem for malformed JSON; 400 validation problem with camelCase keys for missing fields, values that cannot be converted, or validator errors; 404 problem for an unknown payment.

**Architecture and style**
- Each type is in the correct layer. Domain: `ProcessedWebhookEvent`, `PaymentStatus`. Application: the request record, validator, use case, result, outcome enum and the `IWebhookEventRepository` port. Infrastructure: the EF configuration and the repository. Api: the signature check, the options and the endpoint mapping.
- Project references follow `docs/architecture.md`, and the endpoint stays thin.
- Sealed classes, file-scoped namespaces, primary constructors, `required` on the contract, `Async` suffixes, CancellationToken passed through, and comments explain why.
- The one `!` in a test (`ProviderWebhookValidatorTests.cs`, `EventId = eventId!`) has a comment that says why.
- Unit tests reference only Domain and Application.

## Optional
1. `src/AgenticPayments.Domain/Payments/Payment.cs` (code rule / domain invariant): `ChangeStatus` accepts any status, including `Pending`, and any transition, such as Failed to Succeeded from a later, different event. The spec does not define transition rules, so this is acceptable. Consider adding a guard if the spec later defines final states.
2. `src/AgenticPayments.Infrastructure/Persistence/WebhookEventRepository.cs` (code rule, encapsulation): the `payment` parameter is used only for a null check. The status change is saved only because the same scoped `AppDbContext` already tracks the payment. The comment documents this, but it is a hidden dependency. `db.Payments.Update(payment)`, or dropping the parameter, would make the contract explicit.
3. `src/AgenticPayments.Api/Program.cs` (existing limitation): `EnsureCreated` does not add the new `ProcessedWebhookEvents` table to a database that already exists. Fresh databases, including the test containers, are not affected.
4. Different events for the same payment that arrive at the same time: the last one saved wins, because there is no concurrency token. The spec does not ask for one.

Note: the line numbers that the reviewer gave refer to lines in `.agent-input/002-webhook/changes.diff`, not in the source files. They are removed here.
