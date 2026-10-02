# Evidence: 003-out-of-order

- Base commit: `fff08042754d0f7aa2104cbfd78362959eda3df3` (main)
- Branch: spec/003-out-of-order
- Status: PASSED (3 loops)
- Final commit: the commit on `spec/003-out-of-order` that contains this file, merged into `main` with `--no-ff`

## Preflight

| Command | Exit status | Output |
|---|---|---|
| `bash scripts/verify.sh all --results artifacts/preflight` (on main) | 0 | restore, build, format, unit (36/36), integration (55/55), boundary, evidence: all exit=0. Output not committed (artifacts/ is ignored). |

## Steps

| Step | Role | Outcome | Boundary | Output |
|---|---|---|---|---|
| 01-planner | planner | plan written (6 FLAGGED decisions) | First check FAIL on the orchestrator's own report file (see [corrections.md](corrections.md)); second check PASS, 1 path | [agents/01-planner.md](agents/01-planner.md), [boundary/01-planner.result](boundary/01-planner.result) |
| 02-test-writer | test-writer | tests written; build fails on missing production types (expected); PLAN UPDATE NEEDED (nullable `payloadHash` constructor parameter) | PASS, 11 paths | [agents/02-test-writer.md](agents/02-test-writer.md), [boundary/02-test-writer.result](boundary/02-test-writer.result) |
| 03-planner | planner | loop 1: `string? payloadHash` without a guard; new FLAGGED decision on the C1 "own transaction" wording | PASS, 1 path | [agents/03-planner.md](agents/03-planner.md), [boundary/03-planner.result](boundary/03-planner.result) |
| 04-test-writer | test-writer | loop 1: `null!` replaced with `null` in one unit test; no plan gap | PASS, 1 path | [agents/04-test-writer.md](agents/04-test-writer.md), [boundary/04-test-writer.result](boundary/04-test-writer.result) |
| 05-implementer | implementer | code written; no WRONG TEST | PASS, 14 paths | [agents/05-implementer.md](agents/05-implementer.md), [boundary/05-implementer.result](boundary/05-implementer.result) |
| 06-verify | orchestrator | build, format, unit (55/55), integration (71/71) | – | [verify/06-verify/summary.txt](verify/06-verify/summary.txt) |
| 07-test-auditor | test-auditor | FAIL: 2 rule findings (`ThrowsAsync` outside `Assert.Multiple`), 5 optional | PASS, 0 paths | [agents/07-test-auditor.md](agents/07-test-auditor.md), [boundary/07-test-auditor.result](boundary/07-test-auditor.result) |
| 08-test-writer | test-writer | loop 2: 2 rule findings and 1 optional item fixed | PASS, 2 paths | [agents/08-test-writer.md](agents/08-test-writer.md), [boundary/08-test-writer.result](boundary/08-test-writer.result) |
| 09-verify | orchestrator | build, format, unit (55/55), integration (71/71) | – | [verify/09-verify/summary.txt](verify/09-verify/summary.txt) |
| 10-test-auditor | test-auditor | PASS (4 optional items) | PASS, 0 paths | [agents/10-test-auditor.md](agents/10-test-auditor.md), [boundary/10-test-auditor.result](boundary/10-test-auditor.result) |
| 11-reviewer | reviewer | APPROVE (4 optional items) | PASS, 0 paths | [agents/11-reviewer.md](agents/11-reviewer.md), [boundary/11-reviewer.result](boundary/11-reviewer.result) |
| 12-documenter | documenter | README.md, docs/user/payments-api.md, docs/user/provider-webhook.md | PASS, 3 paths | [agents/12-documenter.md](agents/12-documenter.md), [boundary/12-documenter.result](boundary/12-documenter.result) |
| 13-reviewer-docs | reviewer | documentation review: CHANGES (1 accuracy item: "no warning" for concurrent changed resends is not a fixed rule) | PASS, 0 paths | [agents/13-reviewer-docs.txt](agents/13-reviewer-docs.txt) (saved as .txt, see [corrections.md](corrections.md)), [boundary/13-reviewer-docs.result](boundary/13-reviewer-docs.result) |
| 13-claim-check | orchestrator | README claim "step 09-verify … unit 55/55, integration 71/71, exit status 0" compared with `verify/09-verify/summary.txt` (all exit=0) and the TRX counters (55/55, 71/71): matches | – | this file |
| 14-documenter | documenter | loop 3: concurrent changed-resend warning described as "not guaranteed"; README links to verify/09-verify output | PASS, 2 paths | [agents/14-documenter.md](agents/14-documenter.md), [boundary/14-documenter.result](boundary/14-documenter.result) |
| 15-reviewer-docs | reviewer | documentation review: APPROVE (1 optional item); the reviewer confirmed the README check claim against the linked `verify/09-verify/` files | PASS, 0 paths | [agents/15-reviewer-docs.md](agents/15-reviewer-docs.md), [boundary/15-reviewer-docs.result](boundary/15-reviewer-docs.result) |
| 16-final | orchestrator | `scripts/workflow/boundary.sh check 003-out-of-order 00-run orchestrator`: PASS, 108 paths (README.md, docs/user, plans, runs, src, tests); no control changed. Exit status 0. Run again after the link-check correction (see corrections.md); the last result is saved. No new verify run: nothing outside README.md, docs/user/, plans/ and runs/ changed after 09-verify | PASS | [boundary/00-run.result](boundary/00-run.result) |

## Verification

| Step | Command | Exit status | Output |
|---|---|---|---|
| 06-verify | `bash scripts/verify.sh build format unit integration --results runs/003-out-of-order/verify/06-verify` | 0 | [verify/06-verify/](verify/06-verify/) (unit.trx 55/55, integration.trx 71/71) |
| 09-verify | `bash scripts/verify.sh build format unit integration --results runs/003-out-of-order/verify/09-verify` | 0 | [verify/09-verify/](verify/09-verify/) (unit.trx 55/55, integration.trx 71/71) |

## Findings and loops

- Loop 1: test-writer (02) reported PLAN UPDATE NEEDED: the plan's `ProcessedWebhookEvent` constructor takes a non-nullable `payloadHash`, but the fake must be seedable with events stored before this spec (null hash). Sent to the planner, because the plan owns the public signatures; then the test-writer runs again with the updated plan. The orchestrator also asked the planner to record a decision on the test-writer's note that the spec's C1 text says "inside their own transaction".
- Loop 2: test-auditor (07) FAIL, 2 findings of category **rule**: `Assert.ThrowsAsync` is outside the `Assert.Multiple` in `RecordAsync_PaymentNotTracked_ThrowsInvalidOperationException` (integration) and `ExecuteAsync_RecordReturnsPaymentChangedEveryTime_ThrowsInvalidOperationException` (unit). Sent to the test-writer, because the problem is in tests. The auditor's optional item about the assertion that cannot fail (`ProcessProviderWebhookUseCaseTests.cs:176`) was also sent.
- Loop 2, after the test correction: gates passed (09-verify), so the implementer was not called. Next: test-auditor.
- Loop 3: documentation review (13) CHANGES: README.md and docs/user/provider-webhook.md say no warning is ever logged for two concurrent copies of one `eventId` with different payloads; the code can also log it (the `PaymentChanged` path re-evaluates and finds the mismatch). Sent to the documenter, because the problem is in the user documentation.
- No further loops. Test-auditor: PASS (10). Reviewer: APPROVE (11). Documentation review: APPROVE (15).
- Code checks did not run again after 09-verify: the boundary checks for 10 to 15 show only README.md and docs/user/ changed (12, 14), and the orchestrator changed only runs/.
- Orchestrator process note: the orchestrator saved the 01-planner report before the 01-planner boundary check, so the first check failed on that file. See [corrections.md](corrections.md).
- Reviewer optional items not done: concurrent same-`eventId` different-payload race can skip the D5 warning (`DuplicateEvent` path); `varchar(64)` literal repeats `PayloadHashLength`; `TryChangeStatus(Pending)` from `Pending` returns true; `InitializeAsync` gets `CancellationToken.None`.

## Decisions on unclear specs

User decisions D1–D6 are in the spec. The plan applies them as written. FLAGGED decisions from `plans/003-out-of-order.md` section 10:

- FLAGGED Hash input: SHA-256 of `paymentId` and `status` (semantic payload), not the raw body.
- FLAGGED Rows without a hash: a duplicate of an event stored before this spec never logs a warning.
- FLAGGED Concurrent duplicate with another payload: a unique-key rejection gives `Duplicate` with no hash compare and no warning.
- FLAGGED Concurrency control: optimistic `xmin` token and one more evaluation, not a `FOR UPDATE` lock.
- FLAGGED Schema upgrade: idempotent `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` after `EnsureCreated`, not EF Core migrations.
- FLAGGED Same terminal status again with a new eventId: `Ignored`, recorded, 200.
- FLAGGED (added in loop 1, step 03) C1 "inside their own transaction": no explicit transaction. Each request reads in its own implicit transaction on its own scoped context; the `xmin` check on the UPDATE gives the same result as one transaction around read and save. Alternative: unit of work or REPEATABLE READ.
- Not flagged (added in loop 1, step 03): `ProcessedWebhookEvent` takes `string? payloadHash` with no null guard; null only for rows stored before this spec.

## Boundary snapshots

- 00-run: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=6f815c1937b019dec5e969d67bdd85b8ac176ff8 controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 01-planner: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=312e867dd37fd6ac8635a714c91760798ee4ff87 controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 02-test-writer: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=0f9d3d5f0769e68c38d96c9a0aa07768073dafa8 controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 03-planner: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=2a2c2241ac5fe5041eb2eb3e772fc1dc76270b91 controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 04-test-writer: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=a1b07b8b3ef67e33b17e8944541b4f5a95be5e74 controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 05-implementer: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=0fbc42425c04f27dc3e8b65f053145c0560a4b96 controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 07-test-auditor: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=d620380384616e9a07ea6ed971084913f9426b36 controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 08-test-writer: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=69e5981f5dd4eb32074ce0ae2071e0bb3f0a8ba5 controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 10-test-auditor: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=b123cf473ed5be4c0e8d3abe4a33fb432c7a132b controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 11-reviewer: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=2160cead3ffffbba8169b90a196f68f0553babb4 controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 12-documenter: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=d6a7a9993d2a9769ef6dbd9bc49fd3f254a20d60 controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 13-reviewer-docs: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=6f593ca6458cb727ac0804eead308c68b059f3ae controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 14-documenter: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=c0abab65254fb6492bdaf6fcd3fcb990213eca13 controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
- 15-reviewer-docs: `head=fff08042754d0f7aa2104cbfd78362959eda3df3 index=6f815c1937b019dec5e969d67bdd85b8ac176ff8 worktree=989ab64e376711224a7bed5652ca701509244192 controls=ed5ed8716f9020e1b014a613722a5eb3ae117902`
