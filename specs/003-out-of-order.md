# 003-out-of-order

Webhook events can arrive in the wrong sequence or more than one time.

- A non-terminal status must not replace a terminal status (Succeeded, Failed).
- Decide what occurs if Failed arrives after Succeeded. Record the decision.
