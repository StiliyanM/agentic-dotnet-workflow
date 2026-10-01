# 002-webhook

POST /webhooks/provider receives {eventId, paymentId, status}.

- Verify the HMAC-SHA256 signature header with a configured secret.
- Process each eventId only one time (idempotent).
- Update the payment status.
