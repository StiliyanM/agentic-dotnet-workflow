# 001-create-payment

POST /payments with amount, currency and method ("ideal" or "klarna").

It returns paymentId, redirectUrl and status Pending.

- The amount must be more than 0.
- The currency must be a supported currency.
