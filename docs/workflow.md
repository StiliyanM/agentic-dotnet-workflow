# Workflow controls

This file describes the controls of the agent workflow: boundary checks, verification commands and run evidence. `CLAUDE.md` gives the procedure that the orchestrator follows.

## Agent boundary checks

The orchestrator runs `scripts/workflow/boundary.sh` outside the agent that it checks.

| Command | What it does |
|---|---|
| `boundary.sh snapshot <spec-id> <step>` | Saves HEAD, the index tree, the working-tree tree, and a hash of `.git/config` and `.git/hooks` to `.git/agent-boundary/<spec-id>/<step>.state`. |
| `boundary.sh check <spec-id> <step> <role>` | Computes the same values again. Each changed path must match a pattern for the role in `scripts/workflow/allowed-paths.conf`. Exit 0 = pass, 1 = violation, 2 = usage error. |
| `boundary.sh diff <base> <out-file> [<path>...]` | Writes the diff from `<base>` to the working tree, including untracked files. |
| `boundary.sh review-input <base> <spec-id> <kind>` | `<kind>` is `code`, `tests` or `docs`. Writes the input for a read-only agent to `.agent-input/<spec-id>/` with a fixed path list: `changes.diff` (`src/`, `tests/`, `AgenticPayments.slnx`), `tests.diff` (`tests/`) or `docs.diff` (`README.md`, `docs/user/`). It fails if the result contains `plans/` or `runs/`, so the reviewer never gets other agents' reasoning or the run evidence. |

### What the check detects

- Unstaged changes, staged changes and new untracked files, in all paths of the repository. The working-tree tree is made with a temporary index file, so a staged change cannot hide a working-tree change, and the reverse.
- A staged change that the agent then reverts in the working tree (the index tree is compared separately).
- Deleted files.
- A commit by the agent (HEAD moved), also when the commit contains only allowed paths.
- Changes to `.git/config` and to hooks in `.git/hooks`.

### What the check does on a violation

- It exits with 1 and lists each violation.
- It saves evidence in `.git/agent-boundary/<spec-id>/`: `<step>.result`, `<step>.worktree.patch`, `<step>.index.patch` and, when HEAD moved, `<step>.commits.txt`.
- It does not change the working tree, the index or HEAD. The orchestrator stops the run, and the user decides what to keep.

### Limits

These checks find mistakes. They are not a security sandbox. An agent that tries to avoid them can do so.

- **Ignored files are not checked.** `bin/`, `obj/`, `artifacts/`, `.agent-input/` and other paths in `.gitignore` are outside the check.
- **Files outside the repository are not checked.** For example, user-level git config, global tools or the NuGet cache.
- **The snapshot is in `.git/`.** An agent with a shell can change the snapshot file. The orchestrator records the snapshot line in the run evidence, so a later comparison can show a change.
- **Only the state after the step is checked.** A change that an agent makes and then reverts before it finishes is not detected.
- **Side effects are not checked.** For example, network calls, Docker containers or processes that the agent starts.
- **Tool restrictions:** the test-auditor and the reviewer have only Read, Grep and Glob, so they cannot write files. The planner has Write but no shell. The test-writer and the implementer need a shell for `dotnet`, so for them the check is the only control.

### Self-test

`bash scripts/workflow/tests/boundary.test.sh` runs each case in a new temporary repository: an allowed edit, a forbidden unstaged edit, a forbidden staged edit, a staged edit reverted in the working tree, a new forbidden file, a deleted file, an unexpected commit, a new hook, a reviewer edit, a plan for another spec, documenter edits to docs and to code, evidence kept after a violation, the diff command, and the three review inputs (with tracked and untracked files in `plans/` and `runs/`, which must not appear in the code-review diff).

## Verification commands

CI (`.github/workflows/verify.yml`) and local runs use the same script. On Windows, run it in Git Bash.

| Check | Command |
|---|---|
| All checks, in sequence | `bash scripts/verify.sh all` |
| Restore | `bash scripts/verify.sh restore` |
| Build (warnings are errors) | `bash scripts/verify.sh build` |
| Format verification | `bash scripts/verify.sh format` |
| Unit tests | `bash scripts/verify.sh unit` |
| Integration tests (needs Docker) | `bash scripts/verify.sh integration` |
| Boundary check tests | `bash scripts/verify.sh boundary` |
| Evidence link check (tests, then the check on `runs/`, `README.md`, `docs/`) | `bash scripts/verify.sh evidence` |

- The output goes to `artifacts/verify/` (or `--results <dir>`): one log for each step, `unit.trx`, `integration.trx`, and `summary.txt` with `<step> exit=<code>` lines.
- A test step fails when no tests ran, or when a test failed, was skipped, or was not executed. `dotnet test` alone returns exit 0 for a skipped test. The script reads the TRX counters, so a skipped integration test does not count as a pass.
- CI runs on `ubuntu-24.04`, which has Docker. The SDK version comes from `global.json`. CI uploads `artifacts/verify/` as the `verify-results` artifact, also when a check fails.

## Run evidence

The orchestrator owns the evidence. Agents do not write it. For each spec run, it keeps `runs/<spec-id>/`:

| Path | Content |
|---|---|
| `runs/<spec-id>/evidence.md` | The summary below. |
| `runs/<spec-id>/agents/<step>.md` | The final report of the agent, exactly as the agent gave it. No heading, no changed paths, no removed text. Not the conversation transcript. |
| `runs/<spec-id>/verify/<step>/` | The output of `scripts/verify.sh` for that verification: logs, TRX files, `summary.txt`. The logs are committed (`.gitignore` keeps `runs/*/verify/**/*.log`). |
| `runs/<spec-id>/boundary/<step>.*` | The boundary snapshot (`.state`), the state after the step (`.after`) and the result (`.result`). On a violation, also the patches and the commit log. |
| `runs/<spec-id>/corrections.md` | Only when needed: corrections and missing evidence found after the run. |

`evidence.md` format:

```markdown
# Evidence: <spec-id>

- Base commit: <sha of main when the branch started>
- Branch: spec/<spec-id>
- Status: PASSED | FAILED after 3 loops | STOPPED: boundary violation
- Final commit: <sha> | not committed

## Steps

| Step | Role | Outcome | Boundary | Output |
|---|---|---|---|---|
| 01-planner | planner | plan written | PASS, 1 path | agents/01-planner.md |
| 04-verify | orchestrator | build, format, unit, integration | – | verify/04-verify/summary.txt |

## Verification

| Step | Command | Exit status | Output |
|---|---|---|---|
| 04-verify | `bash scripts/verify.sh build format unit integration --results runs/<spec-id>/verify/04-verify` | 0 | verify/04-verify/ |

## Findings and loops

- Loop 1: test-auditor FAIL (<short reason>). Sent to the test-writer.

## Decisions on unclear specs

- <decision>
```

Rules for evidence:
- Record only what a command or an agent actually returned. Do not write a result that no output shows.
- Record the exit status of each command as the shell returned it.
- Do not save conversation transcripts, tokens, passwords or connection strings for real systems.
- Do not change an agent report after it is saved. Put a correction in `corrections.md` and name the file and the problem.
- When an output was not saved, write "missing" and the reason. Do not make the output again and present it as the original.
- Before the commit, run `bash scripts/workflow/check-evidence-links.sh` after `git add`. Each link in the evidence must point to a committed file.
- Runs before this format (specs 001, 004, 005 and 006) have only their entries in `runs/log.md`. No evidence files were made for them later.

## Controls and maintenance

A spec run must not change its own controls: `specs/`, `CLAUDE.md`, `.claude/`, `docs/architecture.md`, `docs/csharp-style.md`, `docs/workflow.md`, `scripts/`, `.github/`, `Directory.Build.props`, `global.json`, `.editorconfig`, `.gitattributes` and `.gitignore`. The `orchestrator` role in `allowed-paths.conf` does not include them. The orchestrator checks the whole run against this role before it commits.

A change to a control is a maintenance change. It happens only when the user asks for it directly, outside a spec run.

### Permission rules

`.claude/settings.json` has `deny` rules for the `Edit` permission on each control path. In Claude Code, `Edit` rules apply to all built-in tools that edit files (Edit and Write), for the main session and for all agents. So no agent and no orchestrator can change a control with these tools.

Limits:
- The rules do not block shell commands. An agent with Bash (test-writer, implementer) can still change a control with a shell command. The boundary check finds this.
- The rules apply only in Claude Code with this project's settings.

To make a maintenance change, the user removes the `deny` rules from `.claude/settings.json` by hand, starts a new session, makes the change, and then restores the rules in the same maintenance change. A spec run never does this.

### When rule changes take effect

Claude Code loads `CLAUDE.md`, the agent definitions and the settings when a session starts. After a maintenance change, start a new session before the next spec run. An agent in the old session can still use its old instructions.
