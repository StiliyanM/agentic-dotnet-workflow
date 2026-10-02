# 003-out-of-order

Webhook events can arrive in the wrong sequence or more than one time.

- A non-terminal status must not replace a terminal status (Succeeded, Failed).
- Decide what occurs if Failed arrives after Succeeded. Record the decision.

## Status of this spec

**Blocked: the decisions in "Open decisions" are not made.** Do not start a run until the user records each decision in this file. The planner must not choose these policies, because they are business decisions, not unclear details.

## Terms

- **Terminal status**: `Succeeded` or `Failed`.
- **Non-terminal status**: `Pending`. Every new payment starts as `Pending` (spec 001).
- **Transition**: a change of a payment status that an event asks for.
- **Applied event**: the event changes the status.
- **Ignored event**: the event is valid and correctly signed, but the status does not change because of a transition rule.
- **Duplicate event**: an event with an `eventId` that is already recorded.

## Contract that does not change (spec 002)

These parts of `POST /webhooks/provider` stay the same. A run for this spec must not change them.

1. The signature check, its header and its order (first). A signature failure gives 401 and changes nothing.
2. The request fields `eventId`, `paymentId`, `status`, and the field validation: 415, 400 problem, 400 validation problem, with the same messages.
3. An unknown `paymentId` gives 404 problem and records nothing.
4. A duplicate event with the same payload gives 200 with no body and changes nothing.
5. An applied event gives 200 with no body.
6. The event record and the status change are saved together or not at all.

## Open decisions

Each decision has a recommendation and its effect. The user decides.

| # | Question | Recommendation | Effect of the recommendation | Main alternative |
|---|---|---|---|---|
| D1 | Does the webhook accept a non-terminal status (`pending`)? | No. Keep 400, as in spec 002. | No contract change. No event can move a payment to `Pending`. | Accept `pending`: a public contract change, and a rule for `Pending` after a terminal status is needed. |
| D2 | `Failed` arrives after `Succeeded`. | Ignore it. The status stays `Succeeded`. | A late or retried failure cannot undo a success. | Apply it (the last event wins): a paid payment can show as failed. |
| D3 | `Succeeded` arrives after `Failed`. | Ignore it. A terminal status is final. | Simple and symmetric with D2. A provider that retries a failed payment with the same `paymentId` cannot mark it as paid. | Apply it: supports provider retries, but D2 and D3 then differ. Check the provider's behavior first. |
| D4 | Is an ignored event recorded as processed? | Yes. | A repeat of the same `eventId` is a duplicate, so the same event always gets the same answer. | Do not record it: a repeat is evaluated again, and an ignored event leaves no trace. |
| D5 | The same `eventId` arrives again with a different `paymentId` or `status`. | 200, nothing changes (as a duplicate). Store a hash of the payload, and log a warning when the hash differs. | No public contract change. A schema change is needed (the event table has no payload column now). | 409 Conflict problem: stricter, but a new public response. Or no detection (spec 002 behavior), which needs no schema change. |
| D6 | HTTP response for an ignored event. | 200 with no body, the same as an applied event. | No public contract change. The provider stops retrying. | A different 2xx code or a body that says "ignored": a public contract change. |

## Acceptance cases

Cases A1–A6 do not depend on an open decision. They must pass with any choice.

| # | Given | When | Then |
|---|---|---|---|
| A1 | A payment in `Pending` | a signed `succeeded` event | the status is `Succeeded`, the event is recorded, 200 |
| A2 | A payment in `Pending` | a signed `failed` event | the status is `Failed`, the event is recorded, 200 |
| A3 | A payment in a terminal status | any request that spec 002 rejects (bad signature, invalid field, unknown payment) | the same response as in spec 002, and nothing changes |
| A4 | A recorded event | the same event again, same payload | 200, nothing changes |
| A5 | A payment in `Succeeded` | a signed `pending` event | the status stays `Succeeded` (with D1 = recommendation: 400, as in spec 002) |
| A6 | Any outcome | an event is recorded or the status changes | both happen in one transaction: never a recorded event without its status change, and never a status change without its recorded event |

Cases that depend on a decision. The run writes the expected result after the decision is recorded:

| # | Given | When | Then | Depends on |
|---|---|---|---|---|
| B1 | A payment in `Succeeded` | a signed `failed` event with a new `eventId` | per D2; the response per D6; the event record per D4 | D2, D4, D6 |
| B2 | A payment in `Failed` | a signed `succeeded` event with a new `eventId` | per D3; the response per D6; the event record per D4 | D3, D4, D6 |
| B3 | B1, then the same `failed` event again | – | 200 and nothing changes if D4 records ignored events; otherwise evaluated again | D4 |
| B4 | A recorded event | the same `eventId` with a different `status` or `paymentId` | per D5 | D5 |

## Concurrency

| # | Given | When | Then |
|---|---|---|---|
| C1 | A payment in `Pending` | a `succeeded` event and a `failed` event with different `eventId`s are processed at the same time | the result is the same as if the events were processed one after the other, in one of the two orders. With D2 and D3 = recommendation: exactly one terminal status is stored, it stays, and the other event is ignored. |
| C2 | A payment in `Pending` | two copies of one event are processed at the same time | the event is recorded once and the status changes once (spec 002 behavior) |

**Test requirement for C1.** The test must force the two operations to overlap at the persistence boundary: both must have read the payment, inside their own transaction, before either one saves. Starting two requests with `Task.WhenAll` is not enough, because they can still run one after the other. The test must also be able to fail: without the concurrency protection, the same test must show a wrong result (for example, the second event replaces the first terminal status).

## Data that exists before this spec

Spec 002 applied any terminal status after any other ("the last event wins"). So a database from before this spec can have a payment that went from `Succeeded` to `Failed` (or the reverse). This spec does not change stored statuses. The new rules apply only to events that arrive after the change. If D5 adds a payload column, events recorded before this spec have no payload hash; they must still count as duplicates.

## Out of scope

- A read endpoint for payments.
- Event ordering by provider timestamps or sequence numbers. The provider sends no timestamp or sequence number (spec 002 fields).
- Refunds and other statuses.
