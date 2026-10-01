# ApmPlayground

Playground to test an agentic development workflow.

- .NET 10 solution `ApmPlayground.slnx`. The code in `src/` follows the layers in `docs/architecture.md` (Domain, Application, Infrastructure, Api). Tests: `tests/ApmPlayground.UnitTests`, `tests/ApmPlayground.IntegrationTests`.
- `docs/architecture.md` contains required rules for all specs. Only I change it, not a run.
- PostgreSQL through EF Core is the only storage. The integration tests need Docker (Testcontainers).
- Specs are in `specs/`. Plans are in `plans/`. The run log is `runs/log.md`.
- Agents are in `.claude/agents/`: planner, test-writer, implementer, test-auditor, reviewer.

## Orchestrator rules

When I write "run spec <id>":

1. Make a git branch spec/<id>.
2. Run the planner, then the test-writer, then the implementer, then `dotnet test`, then the test-auditor, then the reviewer. After the test-writer, the build can fail because the code does not exist yet. This is expected. Do not count it as a failed gate.
3. If a gate fails, send the findings to the correct agent and do the steps again from that agent. Code problems and failed tests go to the implementer. Audit FAIL and tests that the implementer reports as wrong go to the test-writer. Do a maximum of 3 loops.
4. Do not ask me questions during a run. If a spec is not clear, make a decision, record it and continue.
5. Commit when all gates pass. Stop after 3 loops if they do not pass.
6. Add an entry to runs/log.md. Record the spec id, the number of loops, the problems that each gate found, the decisions on unclear specs and the final status.
7. At the end, give me a summary of 5 lines.

## Run procedure

These steps give the details of the orchestrator rules.

**Preflight (before rule 1).** Make sure that the working tree is clean, `docker info` passes, and `dotnet test` passes on `main`. If the `docker` command is not found, add `%LOCALAPPDATA%\Programs\DockerDesktop\resources\bin` to PATH for the command and try again. If one of them fails, stop. Do not make a branch. Do not count it as a loop. Tell me the cause.

**Branch.** Start `spec/<id>` from `main`.

**Snapshot and boundary checks.** Before each agent, run `git add -A`. After the agent finishes, find the files it changed with `git diff --name-only -- <path>` and `git ls-files --others --exclude-standard -- <path>`. Each agent can change only these paths:

| Agent | Can change |
|---|---|
| planner | `plans/` |
| test-writer | `tests/` |
| implementer | `src/`, `ApmPlayground.slnx` |
| test-auditor, reviewer | nothing |

If an agent changes a different path, undo that change (`git checkout -- <path>` and delete the new files). This is a failed gate for that agent. Send the violation to it as a finding.

**Gates after the implementer.** Run in this sequence:
1. `dotnet build` (warnings are errors).
2. `dotnet format --verify-no-changes`.
3. `dotnet test`.

Problems in `src/` go to the implementer. Problems in `tests/` go to the test-writer.

**Audit and review.** Run `git add -A` before the test-auditor and the reviewer, so that `git diff main` shows new files. Send the reviewer only the spec id. Do not send it the plan or the output of the other agents.

**Decisions.** Record the decisions on unclear specs in the "Decisions" section of `plans/<id>.md` and in `runs/log.md`.

**Loops.** A loop is one return to an earlier agent after a failed gate.

**End of a passed run.** Add the log entry, then commit it with the code. Merge `spec/<id>` into `main` with `git merge --no-ff`, so that the next spec starts from this code.

**End of a failed run.** Add the log entry. Commit all work on `spec/<id>` with the message `WIP: spec/<id> failed after 3 loops`. Do not merge into `main`. Go back to `main`.
