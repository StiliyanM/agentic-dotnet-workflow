# Payments API reference

This page describes the API as it is implemented now. The provider webhook (`POST /webhooks/provider`, spec 002) is on its own page: [provider-webhook.md](provider-webhook.md). Spec 003 (events out of order) is planned and is not implemented.

## `POST /payments`

Creates a payment with the status `Pending` and returns a redirect URL.

Source: `src/AgenticPayments.Api/Payments/PaymentEndpoints.cs`, `src/AgenticPayments.Application/Payments/`.

### Request

`Content-Type: application/json`

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
| `paymentId` | string (GUID) | New payment id. |
| `redirectUrl` | string (URL) | `https://pay.example.com/{method}/{paymentId}`, with the method in lower case. This URL is simulated. No provider is called. |
| `status` | string | `Pending` |

There is no `Location` header.

A payment status is `Pending`, `Succeeded` or `Failed`. A new payment is always `Pending`. Only the provider webhook changes it to `Succeeded` or `Failed`. There is no endpoint that reads a payment.

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

A validation problem has `Content-Type: application/problem+json`, `title` `"One or more validation errors occurred."`, `status` 400 and an `errors` object. The keys are `amount`, `currency` and `method`.

When fields are missing and another field has a value that cannot be read, the response contains both errors. When more than one field has a value that cannot be read, the response contains the first one only.

## `GET /health`

Returns `200` when the database is reachable, and `503` when it is not.

## Limits

See [Known limitations](../../README.md#known-limitations) in the README.
