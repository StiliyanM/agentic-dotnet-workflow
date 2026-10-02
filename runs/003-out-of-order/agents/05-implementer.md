I implemented spec 003-out-of-order in `src/`. `bash scripts/verify.sh build format unit integration` passes: the build has 0 warnings and 0 errors, the format check passes, unit tests are 55/55 and integration tests are 71/71 (Docker via Testcontainers), with 0 skipped.

**Files changed (all under C:\Users\Stiliyan\source\repos\ApmPlayground\src\):**
- `AgenticPayments.Domain/Payments/Payment.cs`: `ChangeStatus` is replaced by `bool TryChangeStatus(PaymentStatus)`. When the status is already `Succeeded` or `Failed`, it returns false and changes nothing.
- `AgenticPayments.Domain/Webhooks/ProcessedWebhookEvent.cs`: new nullable `string? payloadHash` constructor parameter and `PayloadHash` property (no null guard), and `PayloadHashLength = 64`.
- `AgenticPayments.Application/Webhooks/WebhookPayloadHash.cs` (new): lower-case hex SHA-256 of `"{paymentId:D}|{status}"`.
- `AgenticPayments.Application/Webhooks/WebhookRecordResult.cs` (new): `Recorded`, `DuplicateEvent`, `PaymentChanged`.
- `AgenticPayments.Application/Webhooks/IWebhookEventRepository.cs`: `FindAsync` and `RecordAsync` replace `ExistsAsync` and `TryRecordAsync`. A comment on `RecordAsync` states that the payment must be tracked by the same scoped context, and that on a conflict the change tracker is cleared.
- `AgenticPayments.Application/Webhooks/ProviderWebhookOutcome.cs` and `ProviderWebhookResult.cs`: added `Ignored` and `DuplicatePayloadMismatch`.
- `AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs`: `MaxAttempts = 2`. It compares the payload hash (a stored row without a hash counts as a duplicate), applies the transition rule, and evaluates the event again after `PaymentChanged`. When the attempts are used up it throws `InvalidOperationException`.
- `AgenticPayments.Infrastructure/Persistence/PaymentConfiguration.cs`: shadow property `builder.Property<uint>("Version").IsRowVersion()`, which maps to the PostgreSQL `xmin` column. `EnsureCreated` did not create a real column, so no explicit column mapping was needed.
- `AgenticPayments.Infrastructure/Persistence/ProcessedWebhookEventConfiguration.cs`: `PayloadHash` has max length 64 and is nullable.
- `AgenticPayments.Infrastructure/Persistence/WebhookEventRepository.cs`: `FindAsync` uses `AsNoTracking`. `RecordAsync` throws `InvalidOperationException` for a payment that the context does not track. A `DbUpdateConcurrencyException` (caught first) clears the tracker and returns `PaymentChanged`. A unique violation clears the tracker and returns `DuplicateEvent`.
- `AgenticPayments.Infrastructure/Persistence/DatabaseInitializer.cs` (new): `EnsureCreatedAsync`, then `ALTER TABLE "ProcessedWebhookEvents" ADD COLUMN IF NOT EXISTS "PayloadHash" character varying(64) NULL`.
- `AgenticPayments.Api/Webhooks/WebhookEndpoints.cs`: the class is now `static partial`, so it can use a `[LoggerMessage]` method. `Ignored` and `DuplicatePayloadMismatch` give 200 with no body. A mismatch logs a Warning in the category `AgenticPayments.Api.Webhooks.WebhookEndpoints` with the template from the plan. The handler now takes an `ILoggerFactory` parameter.
- `AgenticPayments.Api/Program.cs`: calls `DatabaseInitializer.InitializeAsync` instead of `EnsureCreated`.

`AgenticPayments.slnx` did not change, and no file outside `src/` changed.

**Check that the C1 tests can fail:** I removed the `IsRowVersion` line for one run. Both cases of `Webhook_ConcurrentTerminalEvents_KeepFirstSavedStatusAndIgnoreOther` and `RecordAsync_PaymentChangedSinceLoad_ReturnsPaymentChangedAndSavesNothing` then failed. I restored the line before the final gate run; the file is the same as the version described above.

**Test problems:** none. No WRONG TEST.
