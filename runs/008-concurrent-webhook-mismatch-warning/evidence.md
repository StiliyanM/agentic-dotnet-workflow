# Evidence: 008-concurrent-webhook-mismatch-warning

- Base commit: `e0f718900afd683046c9f2fee92580840d1dd7aa` (main)
- Branch: spec/008-concurrent-webhook-mismatch-warning
- Status: PASSED (3 loops)
- Final commit: the commit on `spec/008-concurrent-webhook-mismatch-warning` that contains this file, merged into `main` with `--no-ff`

## Preflight

| Command | Exit status | Output |
|---|---|---|
| `bash scripts/verify.sh all --results artifacts/preflight` (on main) | 0 | restore, build, format, unit (87/87), integration (90/90), boundary, evidence: all passed. Output not committed (artifacts/ is ignored). |

## Steps

| Step | Role | Outcome | Boundary | Output |
|---|---|---|---|---|
| 00-run | orchestrator | snapshot at branch start | – | [boundary/00-run.state](boundary/00-run.state) |
| 01-planner | planner | plan written (1 FLAGGED decision: F1); no DECISION CONFLICT | PASS, 1 path | [agents/01-planner.md](agents/01-planner.md), [boundary/01-planner.result](boundary/01-planner.result) |
| 02-test-writer | test-writer | tests written; no PLAN GAP; build and format pass against current code | PASS, 4 paths | [agents/02-test-writer.md](agents/02-test-writer.md), [boundary/02-test-writer.result](boundary/02-test-writer.result) |
| 03-before-fix | orchestrator | before-fix step: build/format pass; unit 88/90 (2 failed), integration 91/94 (3 failed); C5 fails as required | – | [verify/03-before-fix/](verify/03-before-fix/) |
| 04-implementer | implementer | code written (1 file); no WRONG TEST, no DECISION CONFLICT | First check FAIL on the orchestrator's own report file (see [corrections.md](corrections.md)); second check PASS, 1 path | [agents/04-implementer.md](agents/04-implementer.md), [boundary/04-implementer.result](boundary/04-implementer.result) |
| 05-verify | orchestrator | build, format, unit (90/90), integration (94/94); C3–C6 passed | – | [verify/05-verify/summary.txt](verify/05-verify/summary.txt) |
| 06-test-auditor | test-auditor | FAIL: 1 correctness (`MismatchWarningCount` counts any warning for E, not the exact mismatch warning text), 0 rule, 2 optional | PASS, 0 paths | [agents/06-test-auditor.md](agents/06-test-auditor.md), [boundary/06-test-auditor.result](boundary/06-test-auditor.result) |
| 07-test-writer | test-writer | loop 1: exact warning text counted in C3–C5 (plus count of all warnings for E); C6 counts all warnings for E; sequential mismatch test also checks the exact text; optional 1 applied as a comment (F1); optional 2 not needed; no plan gap | PASS, 2 paths | [agents/07-test-writer.md](agents/07-test-writer.md), [boundary/07-test-writer.result](boundary/07-test-writer.result) |
| 08-verify | orchestrator | build, format, unit (90/90), integration (94/94); C3–C6 passed | – | [verify/08-verify/summary.txt](verify/08-verify/summary.txt) |
| 09-test-auditor | test-auditor | PASS (2 optional items) | PASS, 0 paths | [agents/09-test-auditor.md](agents/09-test-auditor.md), [boundary/09-test-auditor.result](boundary/09-test-auditor.result) |
| 10-reviewer | reviewer | CHANGES: 1 style item (`RacingWebhookEventRepository.RecordAsync` must be expression-bodied), 2 optional | PASS, 0 paths | [agents/10-reviewer.md](agents/10-reviewer.md), [boundary/10-reviewer.result](boundary/10-reviewer.result) |
| 11-test-writer | test-writer | loop 2: `RecordAsync` made expression-bodied; 2 optional items not applied (reasons in report); no plan gap | PASS, 1 path | [agents/11-test-writer.md](agents/11-test-writer.md), [boundary/11-test-writer.result](boundary/11-test-writer.result) |
| 12-verify | orchestrator | build, format, unit (90/90), integration (94/94); C3–C6 passed | – | [verify/12-verify/summary.txt](verify/12-verify/summary.txt) |
| 13-reviewer | reviewer | APPROVE (3 optional items) | PASS, 0 paths | [agents/13-reviewer.md](agents/13-reviewer.md), [boundary/13-reviewer.result](boundary/13-reviewer.result) |
| 14-documenter | documenter | README.md, docs/user/provider-webhook.md (limitation "Changed resends are not always logged" updated) | PASS, 2 paths | [agents/14-documenter.md](agents/14-documenter.md), [boundary/14-documenter.result](boundary/14-documenter.result) |
| 15-reviewer-docs | reviewer | documentation review: CHANGES (2 accuracy items: "the tests cover two copies, not three or more" is false, because `Webhook_ConcurrentDuplicates_ProcessOnce` sends 5 identical copies; limit it to copies with different payloads), 1 optional | PASS, 0 paths | [agents/15-reviewer-docs.md](agents/15-reviewer-docs.md), [boundary/15-reviewer-docs.result](boundary/15-reviewer-docs.result) |
| 16-documenter | documenter | loop 3: coverage sentence limited to copies with different payloads in both files; optional item applied (each other copy with a different payload logs one warning, not tested for 3+) | PASS, 2 paths | [agents/16-documenter.md](agents/16-documenter.md), [boundary/16-documenter.result](boundary/16-documenter.result) |
| 17-reviewer-docs | reviewer | documentation review: APPROVE (3 optional items). The diff has no statement that a check passed, so there is no claim to compare with `runs/` | PASS, 0 paths | [agents/17-reviewer-docs.md](agents/17-reviewer-docs.md), [boundary/17-reviewer-docs.result](boundary/17-reviewer-docs.result) |
| 18-final | orchestrator | `scripts/workflow/boundary.sh check 008-concurrent-webhook-mismatch-warning 00-run orchestrator`: PASS, 98 paths (README.md, docs/user, plans, runs, src, tests); no control changed. Exit status 0. Run again after the evidence and log updates; the last result is saved. No new verify run: steps 13 to 17 changed only README.md and docs/user/ (14, 16), and the orchestrator changed only runs/ | PASS | [boundary/00-run.result](boundary/00-run.result) |

## Verification

| Step | Command | Exit status | Output |
|---|---|---|---|
| 05-verify | `bash scripts/verify.sh build format unit integration --results runs/008-concurrent-webhook-mismatch-warning/verify/05-verify` | 0 | [verify/05-verify/](verify/05-verify/) (unit.trx 90/90; [integration.trx](verify/05-verify/integration.trx) 94/94, shows C3, C4, C5 and C6 passed) |
| 08-verify | `bash scripts/verify.sh build format unit integration --results runs/008-concurrent-webhook-mismatch-warning/verify/08-verify` | 0 | [verify/08-verify/](verify/08-verify/) (unit.trx 90/90; [integration.trx](verify/08-verify/integration.trx) 94/94, shows C3, C4, C5 and C6 passed) |
| 12-verify | `bash scripts/verify.sh build format unit integration --results runs/008-concurrent-webhook-mismatch-warning/verify/12-verify` | 0 | [verify/12-verify/](verify/12-verify/) (unit.trx 90/90; [integration.trx](verify/12-verify/integration.trx) 94/94, shows C3, C4, C5 and C6 passed; the final verification) |

## Before the fix

| Step | Command | Exit status | Output |
|---|---|---|---|
| 03-before-fix | `bash scripts/verify.sh build format --results runs/008-concurrent-webhook-mismatch-warning/verify/03-before-fix` | 0 | [verify/03-before-fix/summary.txt](verify/03-before-fix/summary.txt) |
| 03-before-fix | `bash scripts/verify.sh unit --results runs/008-concurrent-webhook-mismatch-warning/verify/03-before-fix/unit` | 1 | [verify/03-before-fix/unit/unit.trx](verify/03-before-fix/unit/unit.trx) (88/90) |
| 03-before-fix | `bash scripts/verify.sh integration --results runs/008-concurrent-webhook-mismatch-warning/verify/03-before-fix/integration` | 1 | [verify/03-before-fix/integration/integration.trx](verify/03-before-fix/integration/integration.trx) (91/94) |

Failing tests (from the TRX files):
- `ProcessProviderWebhookUseCaseTests.ExecuteAsync_RecordReturnsDuplicateEventStoredPayloadDiffers_ReturnsDuplicatePayloadMismatch(difference: "other status")` (unit/unit.trx)
- `ProcessProviderWebhookUseCaseTests.ExecuteAsync_RecordReturnsDuplicateEventStoredPayloadDiffers_ReturnsDuplicatePayloadMismatch(difference: "other paymentId")` (unit/unit.trx)
- `ProviderWebhookTests.Webhook_ConcurrentSameEventIdOtherPayment_KeepsOtherPaymentPendingAndLogsOneWarning` (integration/integration.trx): warning count expected 1, actual 0
- `ProviderWebhookTests.Webhook_ConcurrentSameEventIdDifferentStatus_FirstSavedWinsAndLogsOneWarning(firstSavedStatus: "succeeded", ...)` (integration/integration.trx): warning count expected 1, actual 0
- `ProviderWebhookTests.Webhook_ConcurrentSameEventIdDifferentStatus_FirstSavedWinsAndLogsOneWarning(firstSavedStatus: "failed", ...)` (integration/integration.trx): warning count expected 1, actual 0

| Case | Expected | Actual |
|---|---|---|
| C5 | fail | fail |
| C3 | pass or fail | fail |
| C4 | pass or fail | fail |
| C6 | pass (guard) | pass |
| A4/B4 and spec 003 race tests | pass | pass |

## Findings and loops

- Loop 1: test-auditor (06) FAIL. Category **correctness** (1): `MismatchWarningCount` in `ProviderWebhookTests` counts any `WebhookEndpoints` warning that contains E, so C3–C5 would still pass if the lost-race path logged a different text (the warning text is part of the unchanged contract). Sent to the test-writer, because the problem is in a test. The 2 optional items were also sent.
- Loop 1, after the test correction: gates passed (08-verify), so the implementer was not called. Next: test-auditor.
- Loop 2: reviewer (10) CHANGES. Style: `tests/AgenticPayments.IntegrationTests/Infrastructure/RacingWebhookEventRepository.cs` `RecordAsync` is one statement in a block after the guard moved to the coordinator; `docs/csharp-style.md` requires an expression body. Sent to the test-writer, because the file is in `tests/`. The 2 optional items were also sent.
- Loop 2, after the test correction: gates passed (12-verify), so the implementer was not called. Next: reviewer again.
- Loop 3: documentation review (15) CHANGES: README.md:185 and docs/user/provider-webhook.md:206 say the tests cover two concurrent copies, not three or more; `Webhook_ConcurrentDuplicates_ProcessOnce` sends 5 identical copies, so the sentence must be limited to copies with different payloads. Sent to the documenter, because the problem is in the user documentation. The optional item was also sent.
- No further loops. Test-auditor: PASS (09). Reviewer: APPROVE (13). Documentation review: APPROVE (17).
- Orchestrator error (not a loop): the 04-implementer report was saved before its boundary check; the first check failed on that file only. See [corrections.md](corrections.md).

## Decisions on unclear specs

From `plans/008-concurrent-webhook-mismatch-warning.md`, section 10 (also in `runs/log.md`):

- E1, E2, E3: user decisions in the spec; followed (R2 logs the warning; the before-fix step ran; C3/C4 were not required to fail before the fix). No DECISION CONFLICT from any agent.
- FLAGGED F1: if the read after the unique-key failure finds no stored event, the result is `Duplicate` (200, no warning). Not reachable with the current code. Alternative (throw, 500) would break required behavior 5.
- Not flagged (agent decision the spec leaves to the test-writer): R1 is selected by payload hash in C3–C5 and by first arrival (atomic claim) in C6.
- Not flagged: fix in the use case (a read after `DuplicateEvent`), no new `WebhookRecordResult` value, no schema change.
