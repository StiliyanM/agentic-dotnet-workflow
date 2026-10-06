# Evidence: 007-create-payment-idempotency

- Base commit: `aefee321b9f8fed5c1de769cdda3dabf1f89c116` (main)
- Branch: spec/007-create-payment-idempotency
- Status: PASSED (2 loops)
- Final commit: the commit on `spec/007-create-payment-idempotency` that contains this file, merged into `main` with `--no-ff`

## Preflight

| Command | Exit status | Output |
|---|---|---|
| `bash scripts/verify.sh all --results artifacts/preflight` (on main, first attempt in this session) | 1 | integration FAILED: "Docker is not running". Run not started; the user started Docker. |
| `bash scripts/verify.sh all --results artifacts/preflight` (on main) | 0 | restore, build, format, unit (55/55), integration (71/71), boundary, evidence: all passed. Output not committed (artifacts/ is ignored). |

## Steps

| Step | Role | Outcome | Boundary | Output |
|---|---|---|---|---|
| 00-run | orchestrator | snapshot at branch start | – | [boundary/00-run.state](boundary/00-run.state) |
| 01-planner | planner | plan written (9 FLAGGED decisions) | PASS, 1 path | [agents/01-planner.md](agents/01-planner.md), [boundary/01-planner.result](boundary/01-planner.result) |
| 02-test-writer | test-writer | tests written; no PLAN GAP; build not run (production types missing, expected) | PASS, 13 paths | [agents/02-test-writer.md](agents/02-test-writer.md), [boundary/02-test-writer.result](boundary/02-test-writer.result) |
| 03-implementer | implementer | code written; no WRONG TEST | PASS, 17 paths | [agents/03-implementer.md](agents/03-implementer.md), [boundary/03-implementer.result](boundary/03-implementer.result) |
| 04-verify | orchestrator | build, format, unit (88/88), integration (93/93) | – | [verify/04-verify/summary.txt](verify/04-verify/summary.txt) |
| 05-test-auditor | test-auditor | FAIL: 2 correctness (integration tests repeat unit-tested rules), 1 rule (cleanup of idempotency records), 3 optional | PASS, 0 paths | [agents/05-test-auditor.md](agents/05-test-auditor.md), [boundary/05-test-auditor.result](boundary/05-test-auditor.result) |
| 06-test-writer | test-writer | loop 1: 3 required findings fixed; 2 of 3 optional applied (hash known-answer test kept, with reason); no plan gap | PASS, 4 paths | [agents/06-test-writer.md](agents/06-test-writer.md), [boundary/06-test-writer.result](boundary/06-test-writer.result) |
| 07-verify | orchestrator | build, format, unit (87/87), integration (90/90) | – | [verify/07-verify/summary.txt](verify/07-verify/summary.txt) |
| 08-test-auditor | test-auditor | PASS (5 optional items) | PASS, 0 paths | [agents/08-test-auditor.md](agents/08-test-auditor.md), [boundary/08-test-auditor.result](boundary/08-test-auditor.result) |
| 09-reviewer | reviewer | APPROVE (6 optional items) | PASS, 0 paths | [agents/09-reviewer.md](agents/09-reviewer.md), [boundary/09-reviewer.result](boundary/09-reviewer.result) |
| 10-documenter | documenter | README.md, docs/user/payments-api.md, docs/user/provider-webhook.md | PASS, 3 paths | [agents/10-documenter.md](agents/10-documenter.md), [boundary/10-documenter.result](boundary/10-documenter.result) |
| 11-reviewer-docs | reviewer | documentation review: CHANGES (1 accuracy item: incomplete list of body errors that come without a key error), 1 optional | PASS, 0 paths | [agents/11-reviewer-docs.md](agents/11-reviewer-docs.md), [boundary/11-reviewer-docs.result](boundary/11-reviewer-docs.result) |
| 11-claim-check | orchestrator | README claim "step 07-verify … unit 87/87, integration 90/90, exit status 0" compared with `verify/07-verify/summary.txt` (all exit=0), unit.log line 10, integration.log line 12 and the TRX counters (87/87, 90/90): matches. The reviewer does not read `runs/`, so the orchestrator did this check | – | this file |
| 12-documenter | documenter | loop 2: complete list of body errors in payments-api.md; README C1 limitation also names the replay and 422 | PASS, 2 paths | [agents/12-documenter.md](agents/12-documenter.md), [boundary/12-documenter.result](boundary/12-documenter.result) |
| 13-reviewer-docs | reviewer | documentation review: APPROVE (2 optional items); README check claim unchanged since 11-claim-check | PASS, 0 paths | [agents/13-reviewer-docs.md](agents/13-reviewer-docs.md), [boundary/13-reviewer-docs.result](boundary/13-reviewer-docs.result) |
| 14-final | orchestrator | `scripts/workflow/boundary.sh check 007-create-payment-idempotency 00-run orchestrator`: PASS, 93 paths (README.md, docs/user, plans, runs, src, tests); no control changed. Exit status 0. Run again after the evidence and log updates: PASS, 97 paths; the last result is saved. No new verify run: steps 08 to 13 changed only README.md and docs/user/ (10, 12), and the orchestrator changed only runs/ | PASS | [boundary/00-run.result](boundary/00-run.result) |

## Verification

| Step | Command | Exit status | Output |
|---|---|---|---|
| 04-verify | `bash scripts/verify.sh build format unit integration --results runs/007-create-payment-idempotency/verify/04-verify` | 0 | [verify/04-verify/](verify/04-verify/) (unit.trx 88/88, integration.trx 93/93) |
| 07-verify | `bash scripts/verify.sh build format unit integration --results runs/007-create-payment-idempotency/verify/07-verify` | 0 | [verify/07-verify/](verify/07-verify/) (unit.trx 87/87, integration.trx 90/90) |

## Findings and loops

- Loop 1: test-auditor (05) FAIL. Category **correctness** (2): `CreatePayment_SameKeyDifferentRequest_Returns422AndCreatesNothing` repeats the currency and method cases that unit tests cover; `CreatePayment_KeyLongerThan100_Returns400` repeats the unit-tested key length rule. Category **rule** (1): `CreatePaymentTests` and `DatabaseInitializerTests` do not clean the idempotency records they create. Sent to the test-writer, because the problems are in tests. The 3 optional items were also sent.
- Loop 1, after the test correction: gates passed (07-verify), so the implementer was not called. Next: test-auditor.
- Loop 2: documentation review (11) CHANGES: docs/user/payments-api.md lists the body errors that come without an `Idempotency-Key` error incompletely (an empty body, invalid JSON and a missing field also return before the header is read). Sent to the documenter, because the problem is in the user documentation. The optional README item about the C1 outcome (409 or replay) was also sent.
- No further loops. Test-auditor: PASS (08). Reviewer: APPROVE (09). Documentation review: APPROVE (13).

## Decisions on unclear specs

From `plans/007-create-payment-idempotency.md`, section 10 (also in `runs/log.md`):

- FLAGGED Overlap gives 409, not a wait-and-replay (C1 gives 201 + 409).
- FLAGGED No separate "processing" state: payment and key record are saved in one transaction.
- FLAGGED A replay always returns `status: Pending`.
- FLAGGED The 400 validation error key is `Idempotency-Key`.
- FLAGGED Body errors come before key errors; key and field rule errors are returned together.
- FLAGGED A whitespace-only key is rejected (400).
- FLAGGED A key is expired at exactly 24h (`>=`).
- FLAGGED Multiple header values are joined with `,` into one key.
- FLAGGED Schema upgrade with raw idempotent SQL in `DatabaseInitializer`, not EF Core migrations.
- Not flagged: `IPaymentRepository.AddAsync` removed; `TimeProvider` injected.
- Test-writer (06): the C1 test requires exactly one 201 and one 409, following the plan; the spec also allows two 201s with the same `paymentId`.
