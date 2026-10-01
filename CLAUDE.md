# ApmPlayground

Playground to test an agentic development workflow.

- .NET 10 solution `ApmPlayground.slnx`: `src/ApmPlayground.Api` (minimal API, EF Core, PostgreSQL), `tests/ApmPlayground.UnitTests`, `tests/ApmPlayground.IntegrationTests`.
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

## Run notes

- Send the reviewer only the spec id. Do not send it the plan or the output of the other agents.
- Record the decisions on unclear specs in the "Decisions" section of `plans/<id>.md` and in `runs/log.md`.
- A loop is one return to an earlier agent after a failed gate.
