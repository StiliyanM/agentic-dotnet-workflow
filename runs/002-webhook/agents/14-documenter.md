# 14-documenter: final report

I updated the user documentation for spec 002-webhook. I changed only `README.md` and `docs/user/`, and I made no commit. The two webhook examples were not run. Both say so, and I make no claim that they were.

## Files changed
- README.md
- docs/user/payments-api.md
- docs/user/provider-webhook.md (new)

## New or changed statements and their sources

**`docs/user/provider-webhook.md` (new page)**
- Request fields and rules. `eventId` is required, not blank and at most 200 characters. `paymentId` is a GUID and not the empty GUID. `status` is `succeeded` or `failed` in any letter case. `pending`, `refunded` and numbers are rejected.
  - Code: `Application/Webhooks/ProviderWebhookRequest.cs`, `ProviderWebhookValidator.cs`, `ProviderPaymentStatus.cs`, `Api/StrictEnumConverter.cs`, `Domain/Webhooks/ProcessedWebhookEvent.cs` (`MaxEventIdLength` = 200).
  - Tests: `Webhook_InvalidStatus_Returns400WithStatusError`, `Webhook_InvalidPaymentId_...`, and the unit validator tests.
- Signature. The API computes HMAC-SHA256 over the raw body bytes, with the UTF-8 bytes of the secret as the key. The header is `X-Provider-Signature: sha256=<64 hex>`, in any hex case, and the API compares it in fixed time. The request gets 401 when the header is missing, has two values, has no prefix, is not hex, has the wrong length or does not match.
  - Code: `Api/Webhooks/ProviderWebhookSignature.cs`.
  - Tests: `Webhook_InvalidSignature_Returns401` (all 6 cases), `Webhook_MissingSignature_...`, `Webhook_UppercaseHexSignature_Returns200`.
- Check order. Signature, then content type, then JSON, then fields, then validator rules, then the duplicate check, then the payment lookup, then save.
  - Code: `Api/Webhooks/WebhookEndpoints.cs`, `Api/JsonRequestBody.cs`, `Application/Webhooks/ProcessProviderWebhookUseCase.cs`.
  - Test: `Webhook_UnsignedMalformedBody_Returns401`.
- Response table (200 with no body, 401/404 problem, 415 with no body, 400 problem, 400 validation problem). The messages are copied from `JsonRequestBody.cs`, `ProviderWebhookValidator.cs` and `ProviderWebhookTests.cs`, for example "Status is required." and "PaymentId has an invalid value.".
- A non-200 response changes nothing, and after a 404 no event is stored.
  - Tests: `AssertUnauthorizedAndNothingChangedAsync`, `Webhook_UnknownPayment_Returns404AndStoresNoEvent`.
- Duplicates. The `ProcessedWebhookEvents` table has `eventId` as its primary key. A repeat returns 200 and does not change the status. Concurrent copies change the payment one time. The event row and the status change are saved in one transaction.
  - Code: `ProcessedWebhookEventConfiguration.cs`, `WebhookEventRepository.cs`, `AppDbContext.cs`.
  - Tests: `Webhook_DuplicateEventId_...`, `Webhook_ConcurrentDuplicates_ProcessOnce`, `TryRecordAsync_EventIdAlreadyStored_...`.
- Status values are `Pending`, `Succeeded`, `Failed`, and `succeeded`/`failed` map to them.
  - Code: `Domain/Payments/PaymentStatus.cs`, `Payment.ChangeStatus`, `ProcessProviderWebhookUseCase.ToPaymentStatus`.
- Configuration. The setting is `Webhooks:Provider:Secret` (environment variable `Webhooks__Provider__Secret`). No committed file contains it. The app does not start without it and shows "Webhooks:Provider:Secret is required.".
  - Code: `Api/Program.cs`, `ProviderWebhookOptions.cs`. I checked both `appsettings*.json` files and `Properties/`.
  - Test: `Startup_WebhookSecretMissing_Fails`.
- Example. A bash command signs the body with `openssl dgst -sha256 -hmac` and sends it with `curl --data-binary`. The response is 200 with no body. The page says the example was not run. There is also a `psql` query to see the status, because no read endpoint exists. The table name `Payments` and the string `Status` column come from `AppDbContext.cs` and `PaymentConfiguration.cs`.
- Schema. A database created before spec 002 has no `ProcessedWebhookEvents` table, and `EnsureCreated` does not add it. The page tells the user to remove the container and start a new one.
  - Code: `Program.cs` (`EnsureCreated`), plan decision D10.
  - The statement "webhook requests then fail with a server error" is not tested. It follows from `UseExceptionHandler` and the missing table.
- Limits. There are no transition rules (`Payment.ChangeStatus` only sets the status). When two different events arrive at the same time, the last one saved wins (reviewer optional item 4). Events have no order. An `eventId` that got a 404 is not stored.
- Planned. Spec 003 is listed as planned and not implemented.

**`docs/user/payments-api.md`**
- The intro now links to the webhook page and says spec 003 is planned. The old text said webhooks were not on the page.
- New lines: the status values are `Pending`/`Succeeded`/`Failed`, a new payment is `Pending`, only the webhook changes the status, and no read endpoint exists. Sources: `PaymentStatus.cs`, `Payment.cs`, `WebhookEndpoints.cs`.

**`README.md`**
- Intro: now mentions the signed provider webhooks.
- Status table: 002 is Completed (orchestrator decision). 003 is still Planned. The note under the table now says spec 003 has no code and a webhook can change any status.
- Setup: the clone URL is now `https://github.com/StiliyanM/agentic-dotnet-workflow.git`, and the text says the rename is done (orchestrator decision).
- Run the API locally: the bash and PowerShell commands now also set `Webhooks__Provider__Secret`. The API does not start without it (`Program.cs`). A note about databases created before spec 002 says to recreate the container.
- Webhook example (new subsection): an openssl and curl command with the expected 200, duplicate 200, 401 and 404 results. It says the command was not run. The results come from `WebhookEndpoints.cs` and the tests.
- Execution evidence: 002 is the first run with `evidence.md`, with a link. Step 11-verify ran build, format, unit 36/36 and integration 55/55 with exit status 0. Source: `runs/002-webhook/evidence.md` (Verification row 11-verify, exit status 0) and `runs/002-webhook/verify/11-verify/summary.txt` (all exit=0).
- Known limitations: two new entries: no status transition rules (spec 003 planned) and the last of two different concurrent events wins. The "No read endpoint" entry now says to query the database to see a status. The `EnsureCreated` entry now covers the database from before spec 002. The authentication entry now says the webhook signature is the only check, and only on the webhook.
- Names table: removed "(rename pending)".

## Statements removed because they are no longer true
- README Status note: "Specs 002 and 003 have no code. The API has no webhook endpoint, and a payment status cannot change from `Pending`."
- README Status table: 002 "**Planned. Not implemented.**"
- README Setup: the clone URL `https://github.com/StiliyanM/apm-playground.git` and "The GitHub repository is going to be renamed ... After the rename, GitHub redirects the old URL."
- README Known limitations: "**No status changes.** Webhooks (specs 002 and 003) are not implemented, so every payment stays `Pending`."
- README Known limitations: "The GitHub repository rename is not done yet (see below)."
- README Names: "(rename pending)".
- README Execution evidence: "This format starts with the next spec run."
- payments-api.md: "Webhooks (specs 002 and 003) are planned and are not on this page."

## Notes for the documentation review
- The examples use the secret value `local-dev-webhook-secret`. This value is only in the documentation and in no config file.
- I did not mention whether `eventId` comparison is case-sensitive, because no test covers it.
