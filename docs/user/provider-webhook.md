# Provider webhook reference

This page describes `POST /webhooks/provider` as it is implemented now (spec 002). Rules for events that arrive out of order (spec 003) are planned and are not implemented. See [Planned](#planned).

Source: `src/AgenticPayments.Api/Webhooks/`, `src/AgenticPayments.Application/Webhooks/`, `src/AgenticPayments.Infrastructure/Persistence/WebhookEventRepository.cs`.

## `POST /webhooks/provider`

The payment provider calls this endpoint to report the result of a payment. A valid, signed event changes the payment status to `Succeeded` or `Failed`. The API processes each `eventId` one time only.

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
| 6 | `eventId` was already processed. | `200`, nothing changes (duplicate) |
| 7 | A payment with `paymentId` exists. | `404` |
| 8 | Change the payment status and store the event. | `200` |

Because step 1 runs first, a request without a valid signature gets `401` even when the body is malformed.

Because step 6 runs before step 7, a repeated `eventId` gets `200` and does not read or change any payment.

Source: `src/AgenticPayments.Api/Webhooks/WebhookEndpoints.cs`, `src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs`.

### Responses

| Condition | Status | Body |
|---|---|---|
| The event is processed | 200 | No body. |
| The `eventId` was already processed (duplicate) | 200 | No body. |
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

A response other than `200` changes no payment and stores no event. After a `404`, the event is not stored, so the provider can send the same `eventId` again later.

Source: `src/AgenticPayments.Api/Webhooks/WebhookEndpoints.cs`, `src/AgenticPayments.Api/JsonRequestBody.cs`, `src/AgenticPayments.Application/Webhooks/ProviderWebhookValidator.cs`. Tests: `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs`, `tests/AgenticPayments.UnitTests/Webhooks/`.

### Duplicate events

The API stores each processed `eventId` in the table `ProcessedWebhookEvents`. `eventId` is the primary key.

- A repeated `eventId` returns `200` with no body. The payment status does not change, also when the repeated event has a different `status`.
- When copies of the same event arrive at the same time, only one copy changes the payment. The other copies return `200`.
- The status change and the event row are saved together, in one transaction. Either both are saved or neither is.

Source: `src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs`, `src/AgenticPayments.Infrastructure/Persistence/WebhookEventRepository.cs`, `src/AgenticPayments.Infrastructure/Persistence/ProcessedWebhookEventConfiguration.cs`. Tests: `Webhook_DuplicateEventId_Returns200AndDoesNotChangeStatusAgain`, `Webhook_ConcurrentDuplicates_ProcessOnce`, `TryRecordAsync_EventIdAlreadyStored_ReturnsFalseAndSavesNothing`.

### Payment status values

A payment status is `Pending`, `Succeeded` or `Failed`. `POST /payments` creates a payment with `Pending`. Only the webhook changes the status. The provider value `succeeded` sets `Succeeded`. The provider value `failed` sets `Failed`.

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

Send the same command again: the response is `200 OK` again, and the status does not change. Send it with a different `eventId` and `"status":"failed"`: the status changes to `Failed` (see [Limits](#limits)).

There is no read endpoint. To see the status, query the database of the README container:

```bash
docker exec agentic-payments-db psql -U postgres -d apm -c 'SELECT "Id", "Status" FROM "Payments";'
```

## Database schema

The API creates the schema at start with `EnsureCreated`. `EnsureCreated` does nothing when the database already has tables. A local database that was created before spec 002 has no `ProcessedWebhookEvents` table, and the API does not add it. Webhook requests then fail with a server error.

Remove the old database once. With the README container, remove the container and start a new one. This deletes all payments in it:

```bash
docker rm -f agentic-payments-db
docker run -d --name agentic-payments-db -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=apm -p 5433:5432 postgres:17-alpine
```

Then start the API again. It creates both tables.

Source: `src/AgenticPayments.Api/Program.cs`, `src/AgenticPayments.Infrastructure/Persistence/AppDbContext.cs`.

## Limits

- **No status transition rules.** Each new `eventId` sets the status. A later event can change `Succeeded` to `Failed`, or `Failed` to `Succeeded`. Source: `Payment.ChangeStatus` in `src/AgenticPayments.Domain/Payments/Payment.cs`.
- **Different events at the same time.** When two events with different `eventId` values for the same payment arrive at the same time, the last one saved wins. There is no concurrency check.
- **No event order.** The API does not use event time or sequence.
- **`eventId` from a 404 is not stored.** A retry with the same `eventId` is processed when the payment exists.
- See also [Known limitations](../../README.md#known-limitations) in the README.

## Planned

Spec [003-out-of-order](../../specs/003-out-of-order.md): webhook events out of sequence. A terminal status is not replaced. This is not implemented.
