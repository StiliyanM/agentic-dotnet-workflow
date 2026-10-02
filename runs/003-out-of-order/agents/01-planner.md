I wrote the plan for spec 003-out-of-order to `C:\Users\Stiliyan\source\repos\ApmPlayground\plans\003-out-of-order.md`. It has all 10 required sections. The user decisions D1–D6 are applied as written, and the spec 002 webhook contract does not change.

**Main design**
- **Status rule (Domain):** `bool Payment.TryChangeStatus(PaymentStatus)` replaces `ChangeStatus`. It returns false and changes nothing when the payment is already `Succeeded` or `Failed`.
- **Payload hash (D5):** `ProcessedWebhookEvent` gets a constructor parameter `payloadHash` and a nullable `string? PayloadHash` (null for events stored before this spec). The new static class `WebhookPayloadHash.Compute(Guid paymentId, ProviderPaymentStatus status)` in Application returns lower-case hex SHA-256 of `"{paymentId:D}|{StatusMemberName}"`.
- **Port change:** `IWebhookEventRepository` now has:
  - `Task<ProcessedWebhookEvent?> FindAsync(string eventId, CancellationToken)`, replacing `ExistsAsync`.
  - `Task<WebhookRecordResult> RecordAsync(ProcessedWebhookEvent, Payment, CancellationToken)`, replacing `TryRecordAsync`. The new enum is `WebhookRecordResult { Recorded, DuplicateEvent, PaymentChanged }`.
- **New outcomes:** `ProviderWebhookOutcome` gets `Ignored` and `DuplicatePayloadMismatch`, each with a factory on `ProviderWebhookResult`. Both map to 200 with no body.
- **Warning log:** the endpoint logs the D5 warning through `ILoggerFactory`. It is not logged in Application, because the architecture rules do not allow Application to reference a logging package.
- **Concurrency (C1):** the `Payments.xmin` system column is the optimistic concurrency token, as a shadow property. On `PaymentChanged`, `RecordAsync` clears the change tracker. The use case then loads the payment and evaluates the event again, at most `MaxAttempts = 2` times. Two attempts are always enough, so the exception after them is only a safety net.
- **Atomic save:** the event and the status change are saved by one `SaveChangesAsync` in `WebhookEventRepository.RecordAsync`. It throws if the payment is not tracked by the same context, so that dependency is now checked and not just assumed.
- **Schema:** the new `DatabaseInitializer.InitializeAsync` in Infrastructure runs `EnsureCreated` and then `ALTER TABLE "ProcessedWebhookEvents" ADD COLUMN IF NOT EXISTS "PayloadHash" varchar(64) NULL`. `Program.cs` calls it, so an existing database gets the column at start. Stored statuses are not changed.
- **C1 test:** an integration test uses `ConfigureTestServices` decorators over the real repositories. Both requests load the payment before either saves, and the test fixes which event saves first. Without the `xmin` token, the second save overwrites the status and the test fails, as the spec requires.

**FLAGGED decisions (please record these in the evidence and the log)**
1. **Hash input:** the hash covers `paymentId` and `status`, not the raw body bytes. With raw bytes, other formatting or status casing would log a false warning.
2. **Events stored before this spec (no hash):** a repeat always counts as a duplicate, with no warning. The alternative is to compare the stored `PaymentId` and warn on a difference.
3. **Concurrent duplicate with a different payload:** when the unique key rejects the insert, the result is `Duplicate` with no hash compare and no warning.
4. **Concurrency control:** optimistic token with one more evaluation. A pessimistic `FOR UPDATE` lock was rejected because the spec's C1 test shape (both read before either saves) is not possible with it.
5. **Schema upgrade:** an idempotent `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` instead of EF Core migrations.
6. **Same terminal status again with a new eventId:** the outcome is `Ignored`, the event is recorded, and the response is 200. A client cannot see a difference from "applied".

**Notes for the next agents**
- Existing tests that call `ChangeStatus`, `TryRecordAsync`, `ExistsAsync` or the two-argument `ProcessedWebhookEvent` constructor must change. The plan lists each one.
- The test-writer must add `CapturingLoggerProvider` and `DatabaseInitializerTests` in the integration tests.
- The implementer should check that Npgsql's `IsRowVersion()` on the shadow `uint` property maps to `xmin` without creating a column under `EnsureCreated`. If it creates one, section 9 gives the explicit mapping to use instead.
