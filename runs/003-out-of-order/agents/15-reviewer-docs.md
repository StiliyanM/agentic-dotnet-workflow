VERDICT: APPROVE

This is the documentation review for spec 003-out-of-order. I checked the docs diff (`README.md`, `docs/user/payments-api.md`, `docs/user/provider-webhook.md`) against the spec, the code in `src/`, the tests in `tests/` and the linked verify output. Nothing needs to change.

**Accuracy**
- **Status rules match `Payment.TryChangeStatus`** (`C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Domain\Payments\Payment.cs`) and `ProcessProviderWebhookUseCase.ExecuteAsync`:
  - Only a `Pending` payment changes.
  - `Succeeded` and `Failed` are final.
  - An ignored event is recorded and gets `200` with no body, and its `eventId` repeated later is a duplicate.
  - `pending` still gets `400`.
  - These match spec decisions D1 to D4 and D6.
- **Check order**: the duplicate check comes before the payment lookup, the 404 records nothing, and the 401 is checked first. This matches `WebhookEndpoints.cs` and the use case.
- **Payload hash** matches `WebhookPayloadHash.Compute`: SHA-256 of `paymentId|status`, 64 lower-case hex characters. The status is an enum, so its letter case and the JSON formatting do not change the hash.
- **Stored events without a hash** still count as duplicates and log no warning (use case lines 34-37).
- **The warning text** matches the `LoggerMessage` in `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Api\Webhooks\WebhookEndpoints.cs` word for word.
- **Concurrency**: the xmin token is the `Version` shadow property with `IsRowVersion` in `PaymentConfiguration.cs`. A `PaymentChanged` result makes the use case evaluate the event again. Both match the "Different events at the same time" section.
- **"A warning is not guaranteed"** for two copies with different payloads that arrive at the same time is correct. A unique-violation result returns `Duplicate` with no warning. A concurrency-conflict result evaluates again and logs the warning.
- **Startup**: `DatabaseInitializer.InitializeAsync` runs `EnsureCreated`, then `ALTER TABLE "ProcessedWebhookEvents" ADD COLUMN IF NOT EXISTS "PayloadHash"`, and `Program.cs` calls it at start. So a database from before spec 002, which has no `ProcessedWebhookEvents` table, makes the statement fail and the API does not start. The docs say this, and they also say that no test covers it.
- **Test names**: all 17 test names cited in the docs exist under `tests/` (integration, unit and `DatabaseInitializerTests`).

**Status**
- Spec 003 is marked Completed. The "Planned" section and the "Not implemented" statements are removed.
- No planned behavior is described as available. Out-of-scope items (ordering by event time, a read endpoint) appear only as limits.

**Check claims**
- The README line in "Execution evidence" for 003 says that step 09-verify ran build, format, unit 55/55 and integration 71/71 with exit status 0. It links `runs/003-out-of-order/evidence.md`, `verify/09-verify/summary.txt`, `unit.log` and `integration.log`. All four files exist, and each claim matches them:
  - `summary.txt`: build, format, unit and integration all have exit=0.
  - `unit.log`: "Failed: 0, Passed: 55, Total: 55".
  - `integration.log`: "Failed: 0, Passed: 71, Total: 71".
- 09-verify is the last verify step in `runs/003-out-of-order/verify/` (the only other one is 06-verify).
- No other statement that a check passed has no link.

**Limits**
- No statement claims production readiness. `README.md` line 5 still says "not production-ready".
- The new limits are stated: a final status cannot be corrected, there is no event order, statuses from before spec 003 stay, some changed resends are not logged, and there are no migrations.

## Optional
- `docs/user/provider-webhook.md`, the sentence "Because step 6 runs before step 7, a repeated `eventId` gets `200` and does not read or change any payment." This text did not change in this diff. It is true for a repeat that arrives later. When copies arrive at the same time, a later copy can read the payment before its save fails as a duplicate. The bullet about copies that arrive at the same time already gives the correct result. The sentence could say "a repeated `eventId` that arrives later".
