# 15-reviewer-docs: final report

VERDICT: APPROVE

Documentation review of `.agent-input/002-webhook/docs.diff` (README.md, docs/user/payments-api.md, new docs/user/provider-webhook.md). I found nothing that must change.

**Accuracy.** I compared each statement in the diff with the code and found no mismatches:
- Route and signature: the route is `/webhooks/provider`. The header is `X-Provider-Signature` with a lower-case `sha256=` prefix.
- How the signature is checked: HMAC-SHA256 over the raw buffered body, keyed with the UTF-8 bytes of the secret, compared in fixed time. Hex in any letter case is accepted, and the value must be exactly 32 bytes. A missing header, several header values, a missing prefix, bad hex or a wrong length all give 401 (ProviderWebhookSignature.cs, WebhookEndpoints.cs).
- Check order: signature (401), then Content-Type (415), then JSON parse or non-object body (400 with no `errors`), then missing or unreadable fields, then the validator, then the duplicate check, then the payment lookup (404), then save. This is the order in WebhookEndpoints.cs, JsonRequestBody.cs and ProcessProviderWebhookUseCase.cs.
- Messages and limits: every message string in the docs matches the validator and JsonRequestBody, including the 200-character limit from `ProcessedWebhookEvent.MaxEventIdLength`. The error keys `eventId`, `paymentId` and `status` match. `pending` and numbers are rejected because `ProviderPaymentStatus` has only Succeeded and Failed.
- Duplicates and storage: the table is `ProcessedWebhookEvents` with `EventId` as primary key. A unique violation gives Duplicate (200). The event and the status change are saved in one SaveChanges call. After a 404 nothing is stored.
- Configuration: the key is `Webhooks:Provider:Secret` (env `Webhooks__Provider__Secret`). Start fails through ValidateOnStart with "Webhooks:Provider:Secret is required." for a missing, empty or white-space value (Program.cs, ProviderWebhookOptions.cs).
- Limits: "no transition rules" is correct, because `Payment.ChangeStatus` only sets the field. "No concurrency check" is correct, because PaymentConfiguration has no concurrency token.
- psql example: `"Payments"` / `"Id"` / `"Status"` match the DbSet name and the string conversion of the status.
- Test names: all seven cited names exist in tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs.
- Rename claim: the README says the repository was renamed to `agentic-dotnet-workflow`. This matches `.git/config` (remote `https://github.com/StiliyanM/agentic-dotnet-workflow.git`). I cannot check GitHub itself.

**Status.** 002 is marked Completed and 003 as "Planned. Not implemented." in the README table, payments-api.md and the "Planned" section of provider-webhook.md. I found no planned behavior described as available.

**Check claim.** There is one statement that a check passed. It links to run evidence, so it is not a CHANGES item, but please compare it with the files:
- README.md line 157 (Execution evidence): "Its last verification (step 11-verify) ran build, format, unit tests (36/36) and integration tests (55/55) with exit status 0." It links to `runs/002-webhook/evidence.md`. Check the step name `11-verify`, the counts 36/36 and 55/55, and exit status 0 against `runs/002-webhook/evidence.md` and `runs/002-webhook/verify/11-verify/`. I did not read `runs/`.

The two curl examples (README "Webhook example" and provider-webhook.md "Example") say plainly that they were not run as part of the verification. That is acceptable and is not a passed-check claim.

**Limits.** The README still says "not production-ready". Known limitations and the provider-webhook.md Limits section list the gaps: no transition rules, last write wins for different events, no event order, EnsureCreated not adding tables, and no auth or rate limiting. Nothing claims production readiness.

## Optional
- provider-webhook.md, check order step 5: the field rules also include `paymentId` not being the all-zero GUID. That rule is documented in the Responses table, so it could also be added to the step 5 text for consistency.
- provider-webhook.md, Duplicate events: you could mention that the event row has a foreign key to the payment with cascade delete (ProcessedWebhookEventConfiguration.cs). This is not required.
