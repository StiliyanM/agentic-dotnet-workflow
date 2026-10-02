# Corrections to the 002-webhook evidence

Written on 2026-10-02, in a maintenance change after the run. The run evidence (`evidence.md`) and the agent reports are not rewritten. This note records what was wrong, what was recovered, and what is still missing.

## 1. Verification logs were not committed

**Problem.** The general `*.log` rule in `.gitignore` excluded the `.log` files under `verify/`. The run committed `summary.txt` and the TRX files, but not the logs. So the link `verify/04-verify/build.log` in `evidence.md` pointed to a file that was not committed.

**Recovered.** The original log files were still in the working tree. They were committed without change on 2026-10-02:

| Folder | Files | File times (2026-10-01, local) |
|---|---|---|
| `verify/04-verify/` | `build.log` | 23:36:22 |
| `verify/07-verify/` | `build.log`, `format.log`, `unit.log`, `integration.log` | 23:39:15 – 23:39:53 |
| `verify/11-verify/` | `build.log`, `format.log`, `unit.log`, `integration.log` | 23:45:27 – 23:46:05 |

The spec commit `6b6a032` has the time 23:55:15. The log times are earlier, so the logs come from the run. No check was run again to make them.

Git stores these files with LF line endings (`.gitattributes`: `* text=auto eol=lf`). Some originals have CRLF line endings. The text is the same. The TRX files that the run committed had the same normalization.

`.gitignore` now keeps `runs/*/verify/**/*.log`. Other `.log` files stay ignored.

## 2. Preflight output recovered

`evidence.md` says that the preflight output was not committed. The original files were still in `artifacts/preflight/` (file times 23:19:48 – 23:22:02 on 2026-10-01, before the run started at 23:22:12). They are now in [verify/00-preflight/](verify/00-preflight/). They were copied, not made again.

## 3. Boundary state files recovered

The run copied only the `.result` files to `boundary/`. The `.state` (snapshot) and `.after` files were still in `.git/agent-boundary/002-webhook/`. They are now in [boundary/](boundary/), with the original file times. The snapshot lines in `evidence.md` can be compared with the `.state` files.

## 4. Reviewer report: line numbers

**Problem.** When the orchestrator saved [agents/13-reviewer.md](agents/13-reviewer.md), it removed the line numbers from the report and added a note. So the saved file was not the agent's report.

**Correction.** On 2026-10-02 the file was restored to the agent's exact text. The line numbers in it are classified here. The reviewer read `.agent-input/002-webhook/changes.diff`, and some numbers are lines in that diff, not lines in the source file:

| Reference in the report | Type | Check |
|---|---|---|
| `src/AgenticPayments.Api/Program.cs:18-21` | Source lines | The options validation and `ValidateOnStart` are on lines 18–21. |
| `src/AgenticPayments.Api/Program.cs:29` | Source line | `EnsureCreated` is on line 29. |
| `ProcessedWebhookEventConfiguration.cs:15` | Source line | `HasKey(e => e.EventId)` is on line 15. |
| `ProcessProviderWebhookUseCase.cs:221` | **Diff line** | The source file has 50 lines. Line 221 of the review diff is the `ExistsAsync` check. |
| `src/AgenticPayments.Domain/Payments/Payment.cs:363` | **Diff line** | The source file has 23 lines. Line 363 of the review diff is `ChangeStatus`. |
| `src/AgenticPayments.Infrastructure/Persistence/WebhookEventRepository.cs:491-512` | **Diff lines** | The source file has 36 lines. Line 491 of the review diff starts `TryRecordAsync`. |

The reviewer instructions now require source paths and source line numbers.

## 5. Other agent reports

When the orchestrator saved the other reports in `agents/`, it added a heading line, changed absolute paths (`C:\Users\...\ApmPlayground\`) to repository-relative paths, and in some reports changed the list layout. The words of the findings were not changed. These files are not restored. The original texts exist only in the orchestrator session.

## 6. Evidence that is still missing

These outputs were not saved during the run, and they no longer exist as files. They are not made again.

| Output | Reason |
|---|---|
| The verification runs of the test-writer and the implementer inside steps 05, 06, 09 and 10 | They wrote to `artifacts/verify/`, and each later run overwrote it. The orchestrator gates 07-verify and 11-verify are complete. |
| The command output of step 14-doc-run (manual run of the README commands) | Only in the orchestrator session. `evidence.md` records the HTTP status lines and the database values. |
| The `.agent-input/002-webhook/*.diff` files that the test-auditor and the reviewer read | `.agent-input/` is ignored and is written again for each step. The code-review diff for 13-reviewer was written by hand with `src/ tests/ AgenticPayments.slnx`. |
