# Payments API reference

This page describes the API as it is implemented now. The provider webhook (`POST /webhooks/provider`, spec 002) is on its own page: [provider-webhook.md](provider-webhook.md). The status rules for webhook events (spec 003) are on the same page.

## `POST /payments`

Creates a payment with the status `Pending` and returns a redirect URL. The `Idempotency-Key` header is required. A retry with the same key does not create a second payment (see [Idempotency](#idempotency)).

Source: `src/AgenticPayments.Api/Payments/PaymentEndpoints.cs`, `src/AgenticPayments.Application/Payments/`.

### Request

Headers:

| Header | Rules |
|---|---|
| `Content-Type` | `application/json` |
| `Idempotency-Key` | Required. 1 to 100 characters. Case-sensitive. A key that is empty or has only whitespace is rejected. Use a new key for each new payment, for example a UUID. |

Example:

```bash
curl -i -X POST http://localhost:5034/payments \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: 6f1c2b7e-0d4a-4c3e-9a51-2f7d8e1b4c90" \
  -d '{"amount": 10.50, "currency": "EUR", "method": "ideal"}'
```

Body:

| Field | Type | Rules |
|---|---|---|
| `amount` | number, or a JSON string that contains a number | Required. More than 0. At most 2 decimal places (trailing zeros such as `10.500` are accepted). At most 9999999999999999.99. |
| `currency` | string | Required. `EUR`, `GBP` or `USD`. Any letter case. |
| `method` | string | Required. `ideal` or `klarna`. Any letter case. |

Property names are matched in any letter case. Other properties are ignored.

For `currency` and `method`, the value must be exactly one name as a JSON string. Numbers (`1`), numeric strings (`"1"`), comma-separated names (`"ideal, klarna"`) and `null` are rejected.

For `amount`, a JSON string that contains a number (`"10.50"`) is accepted. This comes from the JSON Web defaults (`JsonSerializerDefaults.Web`). No test covers it.

### Response `201 Created`

| Field | Type | Value |
|---|---|---|
| `paymentId` | string (GUID) | New payment id. For a replay, the id of the first payment. |
| `redirectUrl` | string (URL) | `https://pay.example.com/{method}/{paymentId}`, with the method in lower case. This URL is simulated. No provider is called. |
| `status` | string | `Pending` |

There is no `Location` header.

A payment status is `Pending`, `Succeeded` or `Failed`. A new payment is always `Pending`. Only the provider webhook changes it to `Succeeded` or `Failed`. `Succeeded` and `Failed` are final: no later webhook event changes them. There is no endpoint that reads a payment.

### Error responses

| Condition | Status | Body |
|---|---|---|
| A required field is missing | 400 | Validation problem. `errors.<field>`: `"<Field> is required."` |
| A field value cannot be read. `amount`: non-numeric string, boolean, `null`. `currency`, `method`: unknown name, number, numeric string, comma list, `null`. | 400 | Validation problem. `errors.<field>`: `"<Field> has an invalid value."` |
| `amount` is 0 or less | 400 | Validation problem. `errors.amount`: `"Amount must be greater than 0."` |
| `amount` is more than 9999999999999999.99 | 400 | Validation problem. `errors.amount`: `"Amount is too large."` |
| `amount` has more than 2 decimal places | 400 | Validation problem. `errors.amount`: `"Amount must have at most 2 decimal places."` |
| The body is empty, is not valid JSON, or is not a JSON object | 400 | Problem (`application/problem+json`) with no `errors` member. |
| `Content-Type` is not JSON | 415 | No body. |
| The `Idempotency-Key` header is missing, empty or only whitespace | 400 | Validation problem. `errors.Idempotency-Key`: `"Idempotency-Key header is required."` |
| The `Idempotency-Key` header has more than 100 characters | 400 | Validation problem. `errors.Idempotency-Key`: `"Idempotency-Key header must be at most 100 characters."` |
| The key was already used with a different request (in the last 24 hours) | 422 | Problem (`application/problem+json`). `status` 422, `detail`: `"The Idempotency-Key was already used with a different request."` No payment is created. |
| Another request with the same key was saved first, at the same time | 409 | Problem (`application/problem+json`). `status` 409, `detail`: `"Another request with the same Idempotency-Key was processed at the same time. Retry the request."` No payment is created by this request. |

A validation problem has `Content-Type: application/problem+json`, `title` `"One or more validation errors occurred."`, `status` 400 and an `errors` object. The keys are `amount`, `currency`, `method` and `Idempotency-Key`.

When fields are missing and another field has a value that cannot be read, the response contains both errors. When more than one field has a value that cannot be read, the response contains the first one only.

The API reads the body before it checks the key. When the body cannot be read, the API returns the body error at once and does not check the key. The response then has no `Idempotency-Key` error, even when the header is missing. The body cannot be read in these cases:
- `Content-Type` is not JSON (415).
- The body is empty, is not valid JSON, or is not a JSON object.
- A required field (`amount`, `currency` or `method`) is missing.
- A field value cannot be read.

For example, `{"currency":"EUR","method":"ideal"}` with no `Idempotency-Key` header gives `400` with `errors.amount` only.

Only the field rule errors (for example `"Amount must be greater than 0."`) are combined with the key errors in one validation problem.

### Idempotency

The API stores each key with the payment that its first request created. The payment and the key are saved in one transaction.

| Case | Response |
|---|---|
| New key | `201`. A new payment. The key is stored. |
| Same key, same request, less than 24 hours after the key was stored | `201` with the first response: the same `paymentId` and `redirectUrl`, and `status` `Pending`. No new payment. |
| Same key, different request, less than 24 hours after the key was stored | `422`. No new payment. The stored key does not change. |
| Same key, 24 hours or more after the key was stored | `201` with a new `paymentId`. The key now points to the new payment. The old payment stays. |
| Invalid request (`400`) | The key is not stored. A corrected request with the same key creates the payment. |
| Two requests with the same new key at the same time | One payment. One request gets `201`. The request whose save comes second gets `409`. A retry of that request then gets the replay (`201`, same `paymentId`) or `422`. |

"Same request" means the same `amount`, `currency` and `method` after parsing. The letter case of `currency` and `method`, the order of the properties, whitespace and the number format of `amount` (`10.5` and `10.50`) do not make a different request. The API compares a SHA-256 hash of these three values.

Keys are case-sensitive: `abc` and `ABC` are two keys.

A replay always has `status` `Pending`, also when a webhook changed the payment to `Succeeded` or `Failed` after the first request. The replay returns the first response, not the current status.

When two requests renew the same expired key at the same time, one request creates a payment and the other gets `409`.

Source: `src/AgenticPayments.Application/Payments/CreatePaymentUseCase.cs`, `src/AgenticPayments.Application/Payments/IdempotencyKeyValidator.cs`, `src/AgenticPayments.Application/Payments/CreatePaymentRequestHash.cs`, `src/AgenticPayments.Domain/Payments/IdempotencyRecord.cs`, `src/AgenticPayments.Infrastructure/Persistence/IdempotencyRecordRepository.cs`. Tests: `tests/AgenticPayments.IntegrationTests/CreatePaymentIdempotencyTests.cs`, `tests/AgenticPayments.UnitTests/Payments/CreatePaymentUseCaseTests.cs`, `tests/AgenticPayments.UnitTests/Payments/IdempotencyKeyValidatorTests.cs`.

Limits of the idempotency key:
- Expired keys are not deleted. They stay in the `IdempotencyRecords` table. There is no cleanup job.
- Payments created before spec 007 have no stored key. They cannot be replayed.
- When the header is sent more than one time, the values are joined with `,` and used as one key. No test covers this.

## `GET /health`

Returns `200` when the database is reachable, and `503` when it is not.

## Limits

See [Known limitations](../../README.md#known-limitations) in the README.
