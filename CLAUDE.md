# AgenticPayments

Playground to test an agentic development workflow.

- .NET 10 solution `AgenticPayments.slnx`. The code in `src/` follows the layers in `docs/architecture.md` (Domain, Application, Infrastructure, Api). Tests: `tests/AgenticPayments.UnitTests`, `tests/AgenticPayments.IntegrationTests`.
- `docs/architecture.md` and `docs/csharp-style.md` contain required rules for all specs. Only I change them, not a run. `.editorconfig` enforces the style rules that an analyzer can check.
- PostgreSQL through EF Core is the only storage. The integration tests need Docker (Testcontainers).
- Specs are in `specs/`. Plans are in `plans/`. The run log is `runs/log.md`.
- Agents are in `.claude/agents/`: planner, test-writer, implementer, test-auditor, reviewer.

## Orchestrator rules

When I write "run spec <id>":

1. Make a git branch spec/<id>.
2. Run the planner, then the test-writer, then the implementer, then `dotnet test`, then the test-auditor, then the reviewer. After the test-writer, the build can fail because the code does not exist yet. This is expected. Do not count it as a failed gate.
3. If a gate fails, send the findings to the correct agent and do the steps again from that agent. Code problems and failed tests go to the implementer. Audit FAIL and tests that the implementer reports as wrong go to the test-writer. If a code problem needs a new or changed test, send it first to the test-writer, then to the implementer. Do a maximum of 3 loops.
4. Do not ask me questions during a run. If a spec is not clear, make a decision, record it and continue.
5. Commit when all gates pass. Stop after 3 loops if they do not pass.
6. Add an entry to runs/log.md. Record the spec id, the number of loops, the problems that each gate found, the decisions on unclear specs and the final status.
7. At the end, give me a summary of 5 lines.

## Run procedure

These steps give the details of the orchestrator rules.

**Preflight (before rule 1).** Make sure that the working tree is clean and that `bash scripts/verify.sh all --results artifacts/preflight` passes on `main`. If the `docker` command is not found, add `%LOCALAPPDATA%\Programs\DockerDesktop\resources\bin` to PATH and try again. If a check fails, stop. Do not make a branch. Do not count it as a loop. Tell me the cause.

**Evidence.** Keep `runs/<id>/` as `docs/workflow.md` describes: `evidence.md` (base commit, one row for each step, verification commands with exit status, findings and loops, decisions), the final report of each agent in `agents/<step>.md`, the verification output in `verify/<step>/`, and the boundary results in `boundary/`. Write only what a command or an agent actually returned. Do not save conversation transcripts or secrets.

**Branch.** Start `spec/<id>` from `main`. Record the base commit in `runs/<id>/evidence.md`. Then run `scripts/workflow/boundary.sh snapshot <id> 00-run`.

**Boundary checks.** Give each agent step a number and a name, for example `03-implementer`. Before the agent starts, run `scripts/workflow/boundary.sh snapshot <id> <step>`. After the agent finishes, run `scripts/workflow/boundary.sh check <id> <step> <role>`. The allowed paths for each role are in `scripts/workflow/allowed-paths.conf`:

| Role | Can change |
|---|---|
| planner | `plans/<id>.md` |
| test-writer | `tests/` |
| implementer | `src/`, `AgenticPayments.slnx` |
| test-auditor, reviewer | nothing (they have no write tools) |

The check compares staged, unstaged and untracked files, HEAD, `.git/config` and `.git/hooks` with the snapshot. An agent must not commit. After each check, copy `.git/agent-boundary/<id>/<step>.result` to `runs/<id>/boundary/`, and save the agent's final report in `runs/<id>/agents/<step>.md`. Update `evidence.md` before the next snapshot.

**On a boundary violation**, stop the run. Do not undo the change, do not delete files, and do not commit. The evidence is in `.git/agent-boundary/<id>/<step>.*`. Write the log entry with the status `STOPPED: boundary violation` and tell me. This is not a loop. I decide what to keep.

**Controls.** A spec run must not change its own controls: `specs/`, `CLAUDE.md`, `.claude/`, `docs/architecture.md`, `docs/csharp-style.md`, `docs/workflow.md`, `scripts/`, `.github/`, `Directory.Build.props`, `global.json`, `.editorconfig`, `.gitattributes`, `.gitignore`. Before the final commit, run `scripts/workflow/boundary.sh check <id> 00-run orchestrator`. It covers the whole run. A change to a control is a maintenance change. It happens only when I ask for it directly, outside a spec run, on `main` or a `maintenance/*` branch.

See `docs/workflow.md` for what the checks do not cover.

**Gates after the implementer.** Run `bash scripts/verify.sh build format unit integration --results runs/<id>/verify/<step>`. It stops at the first failed check. Record the command and its exit status in `evidence.md`.

Problems in `src/` go to the implementer. Problems in `tests/` go to the test-writer.

**Audit and review.** The test-auditor and the reviewer have no shell. Before the test-auditor, run `scripts/workflow/boundary.sh diff <base> .agent-input/<id>/tests.diff tests/`. Before the reviewer, run `scripts/workflow/boundary.sh diff <base> .agent-input/<id>/changes.diff`. `<base>` is the `main` commit that the branch started from. Send the reviewer only the spec id. Do not send it the plan or the output of the other agents.

**Decisions.** Record the decisions on unclear specs in the "Decisions" section of `plans/<id>.md` and in `runs/log.md`.

**Loops.** A loop is one return to an earlier agent after a failed gate.

**End of a passed run.** Add the log entry, then commit it with the code. Merge `spec/<id>` into `main` with `git merge --no-ff`, so that the next spec starts from this code.

**End of a failed run.** Add the log entry. Commit all work on `spec/<id>` with the message `WIP: spec/<id> failed after 3 loops`. Do not merge into `main`. Go back to `main`.
