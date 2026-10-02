I updated the user documentation for spec 003-out-of-order. I changed only README.md and two files in docs/user/. No file outside those paths changed.

**Changed files**
- C:\Users\Stiliyan\source\repos\ApmPlayground\README.md
- C:\Users\Stiliyan\source\repos\ApmPlayground\docs\user\provider-webhook.md (rewritten)
- C:\Users\Stiliyan\source\repos\ApmPlayground\docs\user\payments-api.md

**New or changed statements and their sources**

*Status rules*
- `Succeeded` and `Failed` are final. A later event (either status, including the same status) is ignored, recorded and gets 200 with no body. The response is the same as for an applied event.
  - Code: `Payment.TryChangeStatus` in src/AgenticPayments.Domain/Payments/Payment.cs; ProcessProviderWebhookUseCase.cs (`Ignored`); WebhookEndpoints.cs (Ignored -> `TypedResults.Ok()`).
  - Tests: `Webhook_TerminalPaymentNewEvent_Returns200KeepsStatusAndRecordsEvent`, `TryChangeStatus_FromTerminal_ReturnsFalseAndKeepsStatus`, `ExecuteAsync_TerminalPayment_ReturnsIgnoredKeepsStatusAndRecordsEvent` (has the same-status cases).
- A repeat of an ignored event's `eventId` is a duplicate: 200, nothing changes. Test: `Webhook_IgnoredEventRepeated_Returns200AndChangesNothing`.
- Requests that spec 002 rejects get the same response for a payment with a final status. Test: `Webhook_TerminalPaymentRejectedRequest_ReturnsSpec002ResponseAndChangesNothing`.
- The check order is unchanged. The duplicate check (step 6) still runs before the payment lookup, and step 8 now applies the status rules. Source: ProcessProviderWebhookUseCase.cs.

*Payload hash and duplicates*
- The event table has a `PayloadHash` column: SHA-256 of `paymentId` and `status`, 64 lower-case hex characters. JSON formatting and the letter case of `status` do not change it.
  - Code: WebhookPayloadHash.cs, ProcessedWebhookEventConfiguration.cs.
  - Tests: WebhookPayloadHashTests.cs.
- The same `eventId` with a different `paymentId` or `status` gets 200, nothing changes, and a warning is logged. I copied the message text from the `LoggerMessage` attribute in WebhookEndpoints.cs.
  - Tests: `Webhook_SameEventIdDifferentPayload_Returns200ChangesNothingAndLogsWarning`, `Webhook_SameEventSamePayload_Returns200AndLogsNoWarning`.
- An event recorded before spec 003 has no hash. It counts as a duplicate, with no warning. Test: `Webhook_EventStoredWithoutPayloadHash_CountsAsDuplicate`.

*Different events at the same time*
- The first event saved for a `Pending` payment sets the status. The other event is ignored, recorded and gets 200.
- Mechanism: the `xmin` concurrency token, then the event is evaluated again with the new data.
  - Code: PaymentConfiguration.cs, WebhookEventRepository.cs, ProcessProviderWebhookUseCase.cs.
  - Tests: `Webhook_ConcurrentTerminalEvents_KeepFirstSavedStatusAndIgnoreOther`, `RecordAsync_PaymentChangedSinceLoad_ReturnsPaymentChangedAndSavesNothing`, `ExecuteAsync_RecordReturnsPaymentChanged_LoadsAgainAndIgnoresEvent`.

*Database schema*
- At start the API runs `EnsureCreated`, then `ALTER TABLE ... ADD COLUMN IF NOT EXISTS "PayloadHash"`. It runs on each start, and existing rows stay with a null hash.
  - Code: DatabaseInitializer.cs, Program.cs.
  - Tests: `InitializeAsync_PayloadHashColumnMissing_AddsNullableColumnAndKeepsRows`, `InitializeAsync_RunTwice_DoesNotFail`.
- A database from before spec 002 has no `ProcessedWebhookEvents` table, so the `ALTER TABLE` fails and the API does not start.
  - This comes from reading the code in DatabaseInitializer.cs only. The docs say that no test covers it.
- Stored statuses from before spec 003 do not change. Source: no code changes stored data (DatabaseInitializer.cs only adds a column); the spec section "Data that exists before this spec" requires it.

*Example and limits*
- Example: a new `eventId` with `"status":"failed"` after `succeeded` gets 200, and the status stays `Succeeded`. Source: the same tests as the status rules. The docs still say that the example command was not run.
- New limits:
  - A final status cannot be corrected by the provider (spec decision D3).
  - No event order.
  - No warning for hashless rows or for concurrent copies with different payloads. Code: the `DuplicateEvent` path in ProcessProviderWebhookUseCase.cs returns `Duplicate`, which logs nothing.
  - No migrations, only one `ALTER TABLE`.

*Verification claim*
- README: run 003 step 09-verify ran build, format, unit (55/55) and integration (71/71) with exit status 0.
- Source: the Verification table in runs/003-out-of-order/evidence.md and runs/003-out-of-order/verify/09-verify/summary.txt (all exit=0).

**Removed statements (no longer true)**
- README Status: "Planned. Not implemented." for 003, and "Spec 003 has no code. A webhook event can change any status..."
- README and provider-webhook.md Limits: "No status transition rules. Each new eventId sets the status. A later event can change Succeeded to Failed..."
- README and provider-webhook.md: "Different events at the same time ... the last one saved wins. There is no concurrency check."
- README: "a database from before spec 002 ... webhook requests fail". Now the API does not start.
- README limitation: "EnsureCreated ... does not add new tables or columns to an existing schema." The `PayloadHash` column is now added.
- provider-webhook.md: "Rules for events out of order (spec 003) are planned and not implemented", the "Planned" section, and the example sentence "with a different eventId and failed: the status changes to Failed".
- payments-api.md: "Spec 003 (events out of order) is planned and is not implemented."

**Note for the orchestrator**
I marked 003 as "Completed" in the README Status table, because you said the branch is merged at the end of this run if all checks pass. This follows how 002 was documented. If the run does not merge, change this row back to Planned.
