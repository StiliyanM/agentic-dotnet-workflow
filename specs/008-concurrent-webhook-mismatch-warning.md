# 008-concurrent-webhook-mismatch-warning

> **DRAFT.** Do not run this spec. The user must approve the open decisions and put this file in `specs/`.

Two requests with the same `eventId` and different payloads can arrive at the same time. Decision D5 of spec 003 requires a warning for a different payload. When both requests read before either one saves, the warning can be missing.

## Problem

Current code (main, `d212f57`):

- `ProcessProviderWebhookUseCase.ExecuteAsync` reads the stored event first (`src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs:31`). When two requests read before either one saves, both find no stored event.
- Both requests then call `RecordAsync`. The second save fails. `WebhookEventRepository.RecordAsync` maps a unique-key failure to `DuplicateEvent` (`src/AgenticPayments.Infrastructure/Persistence/WebhookEventRepository.cs:41-46`).
- The use case maps `DuplicateEvent` to `ProviderWebhookResult.Duplicate()` without a hash compare (`ProcessProviderWebhookUseCase.cs:56-57`). The endpoint logs the warning only for `DuplicatePayloadMismatch` (`src/AgenticPayments.Api/Webhooks/WebhookEndpoints.cs:43-46`). So no warning is logged.
- Different `paymentId`: the second save changes another payment row, so the `xmin` check does not fail. Only the unique key fails. The warning is always missing.
- Same `paymentId`, different `status`: the second save can fail on the `xmin` check (`PaymentChanged`; a new read then finds the mismatch and logs the warning) or on the unique key (no warning). The result depends on which failure EF Core reports first.

Records of the problem:

- `runs/log.md`, entry 003, "Decisions on unclear specs": planner decision "FLAGGED Concurrent duplicate with another payload: a unique-key rejection gives `Duplicate` with no hash compare and no warning". This agent decision does not agree with user decision D5 in `specs/003-out-of-order.md`.
- `runs/003-out-of-order/agents/11-reviewer.md`, optional item 1. `runs/log.md`, entry 003, "Open items", item 1.
- `README.md`, "Known limitations", "Changed resends are not always logged".
- `docs/user/provider-webhook.md:202`, the same limitation.

## Contract that does not change (specs 002, 003, 007)

- The signature check, the request fields, the validation and all error responses.
- A duplicate event gives 200 with no body and changes nothing.
- Decisions D1–D6 of spec 003.
- An event recorded before spec 003 (no payload hash) counts as a duplicate and logs no warning.
- The event record and the status change are saved together or not at all.
- The text, the level and the category of the existing warning (`WebhookEndpoints`, `Warning`, "Webhook event {EventId} was received again with a different payload. It was not processed again.").

## Required behavior

1. When a request loses the race for an `eventId`, the API compares its payload hash with the hash of the stored event.
2. A different hash gives the same result as a sequential resend with a different payload: 200, no change, one warning.
3. The same hash gives the same result as a sequential duplicate: 200, no change, no warning.
4. The losing request does not change a payment and does not add an event row.
5. No request in these cases gives 500.

## Acceptance cases

In each case, the test forces both requests to read the stored event and the payment before either one saves. Request R1 saves first. Request R2 saves after R1 has committed.

| # | Given | When (forced overlap) | Then |
|---|---|---|---|
| C3 | Payment P is `Pending` | R1 = (`eventId` E, P, `succeeded`); R2 = (E, P, `failed`) | Both responses are 200. P is `Succeeded`. One event row for E, with the hash of R1. Exactly one mismatch warning for E. |
| C4 | Payment P is `Pending` | R1 = (E, P, `failed`); R2 = (E, P, `succeeded`) | Both responses are 200. P is `Failed`. One event row for E, with the hash of R1. Exactly one mismatch warning for E. |
| C5 | Payments P and Q are `Pending` | R1 = (E, P, `succeeded`); R2 = (E, Q, `succeeded`) | Both responses are 200. P is `Succeeded`. Q stays `Pending`. One event row for E, linked to P. Exactly one mismatch warning for E. |
| C6 | Payment P is `Pending` | R1 and R2 = (E, P, `succeeded`), the same payload | Both responses are 200. P is `Succeeded`. One event row for E. No mismatch warning. |

The sequential cases A4 and B4 of spec 003 and their tests must still pass.

## Verification requirements

1. **Forced overlap.** Each test for C3–C6 forces the overlap at the persistence boundary. `Task.WhenAll` alone is not enough. The current `WebhookRaceCoordinator` (`tests/AgenticPayments.IntegrationTests/Infrastructure/WebhookRaceCoordinator.cs`) selects the first request by `eventId`. In C3–C6 both requests have the same `eventId`, so the test needs another way to select R1 (for example the payload). The test-writer chooses it.
2. **Exactly one warning.** The test counts the mismatch warnings for E in the capturing logger. A check that "a warning exists" is not enough.
3. **Failing evidence before the fix.** After the test-writer step and before the implementer step, the orchestrator runs `bash scripts/verify.sh build format unit integration --results runs/008-concurrent-webhook-mismatch-warning/verify/<step>-before-fix`. It records the command, the exit status and the names of the failing tests from `integration.trx` in `evidence.md`. This run is expected to fail. It is not a failed gate and not a loop.
   - C5 must fail in this run. The current code cannot log the warning in C5.
   - C3 and C4 can pass or fail before the fix. The result depends on which save failure EF Core reports first. Record the actual result. Do not repeat the run to get a different result.
   - C6 is a guard. It can pass before the fix.
   - If C5 passes before the fix, the test cannot detect the defect. Send it to the test-writer. This is a loop.
4. **Passing evidence after the fix.** The normal gates after the implementer pass. `evidence.md` names the `integration.trx` file that shows C3–C6 passed.
5. The documenter changes the limitation in `README.md` and `docs/user/provider-webhook.md` only to match the code and the passing tests.

## Scope

- In scope: the webhook use case, the handling of the repository result, the tests for C3–C6, and the user documentation of the limitation.
- Out of scope: events recorded before spec 003 (no hash). A new response code. A change to the warning text. Event ordering. Any schema change.

## Open decisions

| # | Question | Recommendation | Effect | Alternative and its effect |
|---|---|---|---|---|
| E1 | Which request logs the warning? | R2, the request whose save fails. | One warning for each conflicting request, the same as the sequential case. | Both requests log: two warnings for one conflict. |
| E2 | Is the "before the fix" run a new procedure? | No. The orchestrator runs it as an extra verification under the current rules and records it. `CLAUDE.md` does not forbid an extra verification command. | No control change. | Add a "before-fix" step to `CLAUDE.md` for all bug-fix specs. This is a maintenance change. |
| E3 | Must C3 and C4 fail before the fix? | No (verification requirement 3). | The evidence shows the race as it is. | Require a deterministic failure. The test must then also control which save failure EF Core reports first. This is fragile. |

Implementation details are agent decisions inside this contract. Examples: a new read after `DuplicateEvent`, or a new `WebhookRecordResult` value. They do not need approval.

## Conflicts with permissions or agent boundaries

- None for the code. The fix is in `src/` (implementer). The tests are in `tests/` (test-writer).
- The before-fix run writes to `runs/<id>/verify/`. The `orchestrator` role in `scripts/workflow/allowed-paths.conf` allows `runs/*`.

## Status of this spec

**DRAFT.** Open decisions: E1, E2, E3.