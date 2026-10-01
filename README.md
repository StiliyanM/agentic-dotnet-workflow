# Agentic .NET Development — A Payments Case Study

This repository tests an agentic development workflow in Claude Code. An orchestrator runs specialized agents (planner, test-writer, implementer, test-auditor, reviewer, documenter) on small specs. Each agent works inside fixed boundaries. Tests, checks and a reviewer decide whether a change is accepted.

The case study is a small payments API in .NET 10: create a payment with an alternative payment method (iDEAL or Klarna), and receive a redirect URL. The API is a learning and testing project. It is **not production-ready**.

## Status

| Spec | Description | Status |
|---|---|---|
| [001-create-payment](specs/001-create-payment.md) | `POST /payments`: amount, currency, method. Returns paymentId, redirectUrl, status Pending. | Completed |
| [004-layered-structure](specs/004-layered-structure.md) | Domain, Application, Infrastructure and Api projects. FluentValidation. | Completed |
| [005-typed-payment-contract](specs/005-typed-payment-contract.md) | Typed, non-nullable request and response. Case-insensitive enums. Field errors for bad JSON values. | Completed |
| [006-apply-style-rules](specs/006-apply-style-rules.md) | C# 14 style rules, enforced by analyzers in the build. | Completed |
| [002-webhook](specs/002-webhook.md) | `POST /webhooks/provider` with HMAC-SHA256 signature check and idempotent event processing. | **Planned. Not implemented.** |
| [003-out-of-order](specs/003-out-of-order.md) | Webhook events out of sequence. A terminal status is not replaced. | **Planned. Not implemented.** |

Specs 002 and 003 have no code. The API has no webhook endpoint, and a payment status cannot change from `Pending`.

## Prerequisites

- .NET SDK 10 (`global.json` selects a 10.0 SDK).
- Docker. The integration tests start PostgreSQL with Testcontainers. Running the API locally also needs PostgreSQL.
- Git Bash on Windows (or any bash) for the scripts in `scripts/`.
- Claude Code, only if you want to run the agent workflow.

## Setup

```bash
git clone https://github.com/StiliyanM/apm-playground.git agentic-dotnet-workflow
cd agentic-dotnet-workflow
dotnet restore AgenticPayments.slnx
dotnet build AgenticPayments.slnx
```

The GitHub repository is going to be renamed to `agentic-dotnet-workflow` (see [Names](#names)). After the rename, GitHub redirects the old URL.

## Run the API locally

Start PostgreSQL in Docker. This example uses port 5433 to avoid a conflict with a local PostgreSQL on 5432:

```bash
docker run -d --name agentic-payments-db -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=apm -p 5433:5432 postgres:17-alpine
```

Run the API with a connection string for that container (bash):

```bash
ConnectionStrings__Postgres="Host=localhost;Port=5433;Database=apm;Username=postgres;Password=postgres" dotnet run --project src/AgenticPayments.Api --launch-profile http
```

In PowerShell, set the variable first:

```powershell
$env:ConnectionStrings__Postgres = "Host=localhost;Port=5433;Database=apm;Username=postgres;Password=postgres"
dotnet run --project src/AgenticPayments.Api --launch-profile http
```

The API listens on `http://localhost:5034`. It creates the database schema at startup (`EnsureCreated`). `GET /health` returns 200 when the database is reachable.

Stop and remove the container:

```bash
docker rm -f agentic-payments-db
```

## Example

Request:

```bash
curl -i -X POST http://localhost:5034/payments -H "Content-Type: application/json" -d '{"amount": 10.50, "currency": "EUR", "method": "ideal"}'
```

Response (`201 Created`):

```json
{"paymentId":"01a0f8e9-1f75-7503-9a58-ca8bd6c56f21","redirectUrl":"https://pay.example.com/ideal/01a0f8e9-1f75-7503-9a58-ca8bd6c56f21","status":"Pending"}
```

A request with a missing amount and an unknown currency (`{"currency": "JPY", "method": "ideal"}`) returns `400` with `application/problem+json`:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"amount":["Amount is required."],"currency":["Currency has an invalid value."]},"traceId":"00-cca3d1ad96a9f638f7cdace9cd208a4a-ca6910a613602876-00"}
```

The full request rules and error responses are in [docs/user/payments-api.md](docs/user/payments-api.md).

## Verification

CI ([.github/workflows/verify.yml](.github/workflows/verify.yml)) and local runs use the same script. CI runs each step separately and continues after a failed check when the build passed. Locally, `all` stops at the first failed check:

```bash
bash scripts/verify.sh all
```

| Check | Command |
|---|---|
| Restore | `bash scripts/verify.sh restore` |
| Build (warnings are errors) | `bash scripts/verify.sh build` |
| Format verification | `bash scripts/verify.sh format` |
| Unit tests | `bash scripts/verify.sh unit` |
| Integration tests (needs Docker) | `bash scripts/verify.sh integration` |
| Boundary check tests | `bash scripts/verify.sh boundary` |

The output goes to `artifacts/verify/`. A test step fails when a test is skipped, so missing Docker cannot give a false pass. Details: [docs/workflow.md](docs/workflow.md#verification-commands).

## Agent workflow

You start a run in Claude Code with `run spec <id>`. [CLAUDE.md](CLAUDE.md) has the full procedure. The sequence:

1. **Preflight**: clean working tree, and `scripts/verify.sh all` passes on `main`.
2. **planner** writes `plans/<id>.md`: files, signatures, tests, edge cases, decisions on unclear points.
3. **test-writer** writes the tests from the spec and the plan. The build can fail here, because the code does not exist yet.
4. **implementer** writes the code until the tests pass. It must not change tests.
5. **Gates**: build, format verification, unit tests, integration tests (`scripts/verify.sh`).
6. **test-auditor** checks the tests against the test rules (PASS or FAIL).
7. **reviewer** checks the diff against the spec and the rules, without the reasoning of the other agents (APPROVE or CHANGES).
8. **documenter** updates `README.md` and `docs/user/`. The reviewer checks the documentation for accuracy.
9. **Final check and commit**. The branch `spec/<id>` is merged into `main`.

Correction rules:
- A failed gate sends its findings to the agent that owns the problem. Code problems go to the implementer. Test problems and audit failures go to the test-writer. A code problem that needs a new test goes to the test-writer first.
- Documentation-check findings go back to the documenter.
- A maximum of 3 loops. After that, the run stops and the work is committed as WIP on the branch, not merged.
- The orchestrator does not ask questions during a run. It records decisions on unclear specs.

## Execution evidence

- [runs/log.md](runs/log.md): one entry for each run, with loops, gate findings, decisions and status.
- `runs/<id>/evidence.md`: base commit, each step, each verification command with its exit status, and links to the agent reports and test output. This format starts with the next spec run. Earlier runs (001, 004, 005, 006) have only their `runs/log.md` entries.
- CI: each workflow run uploads `artifacts/verify/` (logs and TRX files) as the `verify-results` artifact.

## Agent boundaries

| Role | Can change | Tools |
|---|---|---|
| planner | `plans/<id>.md` | Read, Grep, Glob, Write |
| test-writer | `tests/` | Read, Grep, Glob, Write, Edit, Bash |
| implementer | `src/`, `AgenticPayments.slnx` | Read, Grep, Glob, Write, Edit, Bash |
| test-auditor | nothing | Read, Grep, Glob |
| reviewer | nothing | Read, Grep, Glob |
| documenter | `README.md`, `docs/user/` | Read, Grep, Glob, Write, Edit |

The orchestrator runs `scripts/workflow/boundary.sh` before and after each agent. The check compares staged, unstaged and untracked files, HEAD, and `.git/config` and hooks with a snapshot. A change outside the allowed paths, or a commit by an agent, stops the run. The check keeps the change as evidence and does not undo it. A spec run must not change its own controls (specs, rules, scripts, CI, build configuration). The final orchestrator check finds such a change. It does not prevent it.

**Limits.** These checks find mistakes. They are not a security sandbox. They do not see ignored files, files outside the repository, side effects such as network calls, or a change that is reverted before the step ends. The test-writer and the implementer need a shell, so for them the boundary check is the only control. Details: [docs/workflow.md](docs/workflow.md#limits).

## Known limitations

- **Simulated provider redirect.** `redirectUrl` is `https://pay.example.com/{method}/{paymentId}`. No payment provider is called. The URL does not lead to a real payment page.
- **No status changes.** Webhooks (specs 002 and 003) are not implemented, so every payment stays `Pending`.
- **No read endpoint.** There is no `GET /payments/{id}`. The `201` response has no `Location` header.
- **Create is not idempotent.** A repeated `POST /payments` creates a second payment. There is no idempotency key.
- **Schema with `EnsureCreated`.** No migrations. When the database already has tables, `EnsureCreated` does nothing. It does not add new tables or columns to an existing schema.
- **`amount` accepts a numeric string.** `"amount": "10.50"` is accepted, but `currency` and `method` reject numeric strings. This comes from the JSON Web defaults. No spec decided it, and no test covers it.
- **No authentication, authorization or rate limiting.**
- **Currencies and methods are fixed.** EUR, GBP, USD; `ideal`, `klarna`. There is no check of which currencies a method supports.
- The GitHub repository rename is not done yet (see below).

## Names

The project had other names before the rename in commit `65b0c4d` (2026-10-01). Historical specs, plans and run-log entries use the old names.

| Item | Old name | New name |
|---|---|---|
| GitHub repository | `apm-playground` | `agentic-dotnet-workflow` (rename pending) |
| Solution | `ApmPlayground.slnx` | `AgenticPayments.slnx` |
| Project and namespace prefix | `ApmPlayground` | `AgenticPayments` |

## Repository layout

| Path | Content |
|---|---|
| `src/` | `AgenticPayments.Domain`, `.Application`, `.Infrastructure`, `.Api` |
| `tests/` | `AgenticPayments.UnitTests`, `AgenticPayments.IntegrationTests` |
| `specs/`, `plans/` | Specs (input) and plans (planner output) |
| `runs/` | Run log and run evidence |
| `docs/` | Architecture, style and workflow rules; user documentation in `docs/user/` |
| `.claude/agents/` | Agent definitions |
| `scripts/` | Verification and boundary-check scripts |
