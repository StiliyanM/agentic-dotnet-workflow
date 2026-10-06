# 007-create-payment-idempotency

POST /payments takes an `Idempotency-Key` header, so a client can retry a request without creating a second payment.

## Contract that does not change (specs 001, 005)

- The request fields, their validation and the error responses.
- A new payment gives 201 with `paymentId`, `redirectUrl` and status Pending.
- The webhook endpoint (specs 002, 003).

## Rules

Each rule has its reason.

- The header is required. A missing or empty key gives 400. Reason: a create without a key can still make a second payment on retry.
- The key is a string of 1 to 100 characters, case-sensitive. Reason: clients can use their own IDs or UUIDs.
- The same key with the same request returns the first response: 201, same `paymentId` and `redirectUrl`. No new payment.
- "Same request" means the same parsed `amount`, `currency` and `method` (a hash, as with the webhook payload). Reason: JSON formatting and case do not make a different request.
- The same key with a different request gives 422 problem and creates nothing. Reason: the caller is the client, not a provider that retries, so a client bug must be visible. The webhook gives 200 for this case only because providers retry non-2xx responses.
- A request with a key that another request is still processing gives 409 problem. The client can retry. Reason: simple and the common industry behavior.
- An invalid request (400) does not store the key, so a corrected request with the same key creates the payment.
- A key is kept for 24 hours. After that, the same key creates a new payment. Cleanup of old keys is out of scope.

## Acceptance cases

| # | Given | When | Then |
|---|---|---|---|
| A1 | No request with key K | a valid request with key K | 201, one payment, the key is stored |
| A2 | A1 done | the same request with key K | 201 with the same `paymentId` and `redirectUrl`, no new payment |
| A3 | A1 done | key K with a different amount | 422, no new payment |
| A4 | Any | a request without the header, or with an empty key | 400 |
| A5 | Any | an invalid request with key K, then a valid one with key K | 400, then 201 with one payment |
| A6 | A1 done more than 24 hours ago | the same request with key K | 201 with a new `paymentId` |
| C1 | No request with key K | two identical requests with key K at the same time | exactly one payment; one response is 201, the other is 201 with the same `paymentId` or 409 |

**Test requirement for C1.** As in spec 003: the test must force both requests to pass the key lookup before either one saves. `Task.WhenAll` alone is not enough. Without the protection, the same test must show two payments.

## Out of scope

- The cleanup job for expired keys.
- A read endpoint for payments.