# 005-typed-payment-contract

Change the `POST /payments` contract to typed, non-nullable fields, as `docs/architecture.md` says.

Request:
- `amount`: decimal, required.
- `currency`: enum of the supported currencies (EUR, GBP, USD), required.
- `method`: enum (ideal, klarna), required.

Response:
- `paymentId`, `redirectUrl` and `status` (Pending). Use typed values, not strings, in the response contract.

Rules:
- Method and currency are case-insensitive. For example, "IDEAL", "Ideal", "eur" and "Eur" are accepted.
- A currency or method that is not in the enum is rejected. A numeric value (for example `"method": 1`) is rejected.
- A missing field is rejected.
- Each rejection gives 400 as a validation problem, with the error under the key of that field (`amount`, `currency` or `method`).
- The amount rules from spec 001 do not change: more than 0, at most 2 decimal places, at most 9999999999999999.99.
- A malformed JSON body gives 400.
