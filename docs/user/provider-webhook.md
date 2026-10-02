# Provider webhook reference

This page describes `POST /webhooks/provider` as it is implemented now: the endpoint (spec 002) and the status rules for events that arrive out of order or more than one time (spec 003).

Source: `src/AgenticPayments.Api/Webhooks/`, `src/AgenticPayments.Application/Webhooks/`, `src/AgenticPayments.Domain/Payments/Payment.cs`, `src/AgenticPayments.Infrastructure/Persistence/WebhookEventRepository.cs`.

## `POST /webhooks/provider`

The payment provider calls this endpoint to report the result of a payment. A valid, signed event changes a `Pending` payment to `Succeeded` or `Failed`. `Succeeded` and `Failed` are final. The API processes each `eventId` one time only.

### Request

Headers:

| Header | Value |
|---|---|
| `Content-Type` | `application/json` |
| `X-Provider-Signature` | `sha256=<hex>`. See [Signature](#signature). |

Body:

| Field | Type | Rules |
|---|---|---|
| `eventId` | string | Required. Not empty and not only white space. At most 200 characters. |
| `paymentId` | string (GUID) | Required. The `paymentId` from `POST /payments`. Not `00000000-0000-0000-0000-000000000000`. |
| `status` | string | Required. `succeeded` or `failed`. Any letter case. |

`pending`, other names (for example `refunded`), numbers and numeric strings are rejected for `status`.

### Signature

1. Take the exact bytes of the request body. Do not parse, reformat or trim the body. A change of one byte (for example a space or a line break) gives a different signature.
2. Compute HMAC-SHA256 over these bytes. The key is the UTF-8 bytes of the configured secret (see [Configuration](#configuration)).
3. Write the 32-byte result as 64 hex characters. Upper case and lower case are both accepted.
4. Send the header `X-Provider-Signature: sha256=<hex>`. The prefix `sha256=` is lower case.

The API compares the signature in fixed time. The request gets `401` when:
- the header is missing;
- the header has more than one value;
- the value does not start with `sha256=`;
- the part after `sha256=` is not hex, or is not exactly 32 bytes;
- the signature does not match the body and the secret.

Source: `src/AgenticPayments.Api/Webhooks/ProviderWebhookSignature.cs`. Tests: `Webhook_InvalidSignature_Returns401`, `Webhook_MissingSignature_Returns401AndChangesNothing`, `Webhook_UppercaseHexSignature_Returns200` in `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs`.

### Check order

The API checks a request in this order. The first failed check gives the response.

| Step | Check | Result when the check fails |
|---|---|---|
| 1 | Signature. Runs before the body is parsed. | `401` |
| 2 | `Content-Type` is JSON. | `415` |
| 3 | The body is valid JSON and a JSON object. | `400` |
| 4 | Each field is present and has a value that can be read. | `400` validation problem |
| 5 | Field rules (`eventId` length, empty values). | `400` validation problem |
| 6 | `eventId` was already recorded. | `200`, nothing changes (duplicate) |
| 7 | A payment with `paymentId` exists. | `404` |
| 8 | Apply the [status rules](#status-rules) and record the event. | – (`200`) |

Because step 1 runs first, a request without a valid signature gets `401` even when the body is malformed. This is also true for a payment that already has a final status.

Because step 6 runs before step 7, a repeated `eventId` gets `200` and does not read or change any payment.

Source: `src/AgenticPayments.Api/Webhooks/WebhookEndpoints.cs`, `src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs`. Test: `Webhook_TerminalPaymentRejectedRequest_ReturnsSpec002ResponseAndChangesNothing`.

### Status rules

| Payment status | Event `status` | Result |
|---|---|---|
| `Pending` | `succeeded` | The status changes to `Succeeded`. The event is recorded. `200`. |
| `Pending` | `failed` | The status changes to `Failed`. The event is recorded. `200`. |
| `Succeeded` | `failed` or `succeeded` | The status stays `Succeeded`. The event is recorded. `200`. |
| `Failed` | `succeeded` or `failed` | The status stays `Failed`. The event is recorded. `200`. |

An event that does not change the status is an ignored event. The response for an ignored event is the same as for an applied event: `200` with no body. The provider cannot see from the response that the event was ignored.

An ignored event is recorded like an applied event. When the provider sends its `eventId` again, it is a duplicate (see [Duplicate events](#duplicate-events)).

The webhook cannot set `Pending`. A `pending` event gets `400` (see [Request](#request)).

The API does not use event time or sequence. The provider sends neither. The first event that is saved for a `Pending` payment sets the final status.

Source: `Payment.TryChangeStatus` in `src/AgenticPayments.Domain/Payments/Payment.cs`, `src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs`, `src/AgenticPayments.Api/Webhooks/WebhookEndpoints.cs`. Tests: `Webhook_ValidSignedEvent_Returns200AndUpdatesStatus`, `Webhook_TerminalPaymentNewEvent_Returns200KeepsStatusAndRecordsEvent`, `Webhook_IgnoredEventRepeated_Returns200AndChangesNothing` in `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs`; `TryChangeStatus_FromTerminal_ReturnsFalseAndKeepsStatus` in `tests/AgenticPayments.UnitTests/Payments/PaymentTests.cs`; `ExecuteAsync_TerminalPayment_ReturnsIgnoredKeepsStatusAndRecordsEvent` in `tests/AgenticPayments.UnitTests/Webhooks/ProcessProviderWebhookUseCaseTests.cs`.

### Responses

| Condition | Status | Body |
|---|---|---|
| The event is applied (the status changes) | 200 | No body. |
| The event is ignored (the payment already has `Succeeded` or `Failed`) | 200 | No body. |
| The `eventId` was already recorded (duplicate) | 200 | No body. |
| The signature is missing or not valid | 401 | Problem (`application/problem+json`) with `status` 401. No detail. |
| `Content-Type` is not JSON | 415 | No body. |
| The body is empty, is not valid JSON, or is not a JSON object | 400 | Problem with no `errors` member. |
| A field is missing | 400 | Validation problem. `errors.<field>`: `"EventId is required."`, `"PaymentId is required."` or `"Status is required."` |
| `paymentId` is not a GUID, or `status` is not `succeeded` or `failed` | 400 | Validation problem. `errors.<field>`: `"PaymentId has an invalid value."` or `"Status has an invalid value."` |
| `eventId` is empty or only white space | 400 | Validation problem. `errors.eventId`: `"EventId is required."` |
| `eventId` is longer than 200 characters | 400 | Validation problem. `errors.eventId`: `"EventId must be at most 200 characters."` |
| `paymentId` is `00000000-0000-0000-0000-000000000000` | 400 | Validation problem. `errors.paymentId`: `"PaymentId is required."` |
| No payment has this `paymentId` | 404 | Problem with `status` 404. |

A validation problem has `Content-Type: application/problem+json`, `title` `"One or more validation errors occurred."`, `status` 400 and an `errors` object. The keys are `eventId`, `paymentId` and `status`.

A response other than `200` changes no payment and records no event. After a `404`, the event is not recorded, so the provider can send the same `eventId` again later.

Source: `src/AgenticPayments.Api/Webhooks/WebhookEndpoints.cs`, `src/AgenticPayments.Api/JsonRequestBody.cs`, `src/AgenticPayments.Application/Webhooks/ProviderWebhookValidator.cs`. Tests: `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs`, `tests/AgenticPayments.UnitTests/Webhooks/`.

### Duplicate events

The API records each processed `eventId` in the table `ProcessedWebhookEvents`. `eventId` is the primary key. Each row also has `PayloadHash`: the SHA-256 hash (64 lower-case hex characters) of the `paymentId` and the `status` of the event. JSON formatting and the letter case of `status` do not change the hash.

- A repeated `eventId` returns `200` with no body. Nothing changes.
- When the repeated event has the same `paymentId` and `status`, the API logs nothing.
- When the repeated event has a different `paymentId` or `status`, the API still returns `200` and changes nothing. It logs a warning: `Webhook event {EventId} was received again with a different payload. It was not processed again.`
- An event that was recorded before spec 003 has no `PayloadHash`. A repeat of its `eventId` is a duplicate. The API logs no warning for it, also when the payload is different.
- When copies of the same event arrive at the same time, only one copy changes the payment. The other copies return `200`.
- The status change and the event row are saved together, in one transaction. Either both are saved or neither is.

Source: `src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs`, `src/AgenticPayments.Application/Webhooks/WebhookPayloadHash.cs`, `src/AgenticPayments.Api/Webhooks/WebhookEndpoints.cs`, `src/AgenticPayments.Infrastructure/Persistence/WebhookEventRepository.cs`, `src/AgenticPayments.Infrastructure/Persistence/ProcessedWebhookEventConfiguration.cs`. Tests: `Webhook_DuplicateEventId_Returns200AndDoesNotChangeStatusAgain`, `Webhook_SameEventSamePayload_Returns200AndLogsNoWarning`, `Webhook_SameEventIdDifferentPayload_Returns200ChangesNothingAndLogsWarning`, `Webhook_EventStoredWithoutPayloadHash_CountsAsDuplicate`, `Webhook_ConcurrentDuplicates_ProcessOnce`, `RecordAsync_EventIdAlreadyStored_ReturnsDuplicateEventAndSavesNothing` in `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs`; `tests/AgenticPayments.UnitTests/Webhooks/WebhookPayloadHashTests.cs`.

### Different events at the same time

Two events with different `eventId` values can arrive for the same `Pending` payment at the same time, for example `succeeded` and `failed`. The result is the same as when the events arrive one after the other:

- The first event that is saved sets the status. The status stays.
- The other event is ignored and recorded. It also gets `200`.

The payment row has a concurrency check (the PostgreSQL system column `xmin`). When the payment changed after the API read it, the save fails, and the API evaluates the event again with the new data.

Source: `src/AgenticPayments.Infrastructure/Persistence/PaymentConfiguration.cs`, `src/AgenticPayments.Infrastructure/Persistence/WebhookEventRepository.cs`, `src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs`. Tests: `Webhook_ConcurrentTerminalEvents_KeepFirstSavedStatusAndIgnoreOther`, `RecordAsync_PaymentChangedSinceLoad_ReturnsPaymentChangedAndSavesNothing` in `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs`; `ExecuteAsync_RecordReturnsPaymentChanged_LoadsAgainAndIgnoresEvent` in `tests/AgenticPayments.UnitTests/Webhooks/ProcessProviderWebhookUseCaseTests.cs`.

### Payment status values

A payment status is `Pending`, `Succeeded` or `Failed`. `POST /payments` creates a payment with `Pending`. Only the webhook changes the status, and only from `Pending`. The provider value `succeeded` sets `Succeeded`. The provider value `failed` sets `Failed`.

Source: `src/AgenticPayments.Domain/Payments/PaymentStatus.cs`, `src/AgenticPayments.Domain/Payments/Payment.cs`.

## Configuration

The webhook secret has the configuration key `Webhooks:Provider:Secret`. As an environment variable, the name is `Webhooks__Provider__Secret`.

- No committed file contains a webhook secret. You must set it yourself, for example with an environment variable.
- The API does not start without it. When the secret is missing, empty or only white space, the start fails with the message `Webhooks:Provider:Secret is required.`

Source: `src/AgenticPayments.Api/Program.cs`, `src/AgenticPayments.Api/Webhooks/ProviderWebhookOptions.cs`. Test: `Startup_WebhookSecretMissing_Fails`.

The commands to run the API with the secret are in the [README](../../README.md#run-the-api-locally).

## Example

This example uses the API from the README local run, with the secret `local-dev-webhook-secret`. Use your own secret value. The example was not run as part of the verification.

1. Create a payment with `POST /payments` and copy the `paymentId` from the response.
2. Sign the body and send it (bash, needs `openssl`):

```bash
SECRET='local-dev-webhook-secret'
PAYMENT_ID='<paymentId from step 1>'
BODY='{"eventId":"evt_001","paymentId":"'"$PAYMENT_ID"'","status":"succeeded"}'
SIGNATURE=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | sed 's/^.* //')
curl -i -X POST http://localhost:5034/webhooks/provider \
  -H "Content-Type: application/json" \
  -H "X-Provider-Signature: sha256=$SIGNATURE" \
  --data-binary "$BODY"
```

`printf '%s'` adds no line break, and `--data-binary` sends the bytes without a change. So the API receives the same bytes that were signed.

Response: `200 OK` with no body. The payment status is now `Succeeded`.

Send the same command again: the response is `200 OK` again, and the status does not change. Send it with a new `eventId` (for example `evt_002`) and `"status":"failed"`: the response is `200 OK`, and the status stays `Succeeded`.

There is no read endpoint. To see the status, query the database of the README container:

```bash
docker exec agentic-payments-db psql -U postgres -d apm -c 'SELECT "Id", "Status" FROM "Payments";'
```

## Database schema

At start, the API creates the schema with `EnsureCreated`. Then it adds the column `PayloadHash` to `ProcessedWebhookEvents` when the column does not exist (`ALTER TABLE ... ADD COLUMN IF NOT EXISTS`). This step runs on each start and changes nothing when the column exists.

- **Database created by spec 002.** The API adds `PayloadHash` at start. The existing event rows stay, with no hash. Stored payment statuses do not change. A payment that went from `Succeeded` to `Failed` (or the reverse) before spec 003 keeps its status. The new rules apply only to events that arrive after the change.
- **Database created before spec 002.** `EnsureCreated` does nothing when the database already has tables, so the `ProcessedWebhookEvents` table does not exist. The `ALTER TABLE` statement then fails, and the API does not start. No test covers this case.

Remove a database from before spec 002 once. With the README container, remove the container and start a new one. This deletes all payments in it:

```bash
docker rm -f agentic-payments-db
docker run -d --name agentic-payments-db -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=apm -p 5433:5432 postgres:17-alpine
```

Then start the API again. It creates both tables.

Source: `src/AgenticPayments.Api/Program.cs`, `src/AgenticPayments.Infrastructure/Persistence/DatabaseInitializer.cs`, `src/AgenticPayments.Infrastructure/Persistence/AppDbContext.cs`. Tests: `InitializeAsync_PayloadHashColumnMissing_AddsNullableColumnAndKeepsRows`, `InitializeAsync_RunTwice_DoesNotFail` in `tests/AgenticPayments.IntegrationTests/DatabaseInitializerTests.cs`.

## Limits

- **A final status cannot be corrected by the provider.** When a provider retries a failed payment with the same `paymentId`, a later `succeeded` event is ignored. The payment stays `Failed`. The response is `200`, so the provider does not see this.
- **No event order.** The API does not use event time or sequence. The first event that is saved for a `Pending` payment wins, also when the provider sent it later.
- **No warning for some changed resends.** There is no warning when the stored event has no `PayloadHash` (recorded before spec 003). When two copies of one `eventId` with different payloads arrive at the same time, a warning is not guaranteed. It can be missing. This depends on which save fails first. Both copies get `200`, and only one copy changes the payment. No test covers the warning in this case.
- **`eventId` from a 404 is not recorded.** A retry with the same `eventId` is processed when the payment exists.
- **No migrations.** The schema change for spec 003 is one `ALTER TABLE` statement at start, not an EF Core migration.
- See also [Known limitations](../../README.md#known-limitations) in the README.
