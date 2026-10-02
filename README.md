# Agentic .NET Development — A Payments Case Study

This repository tests an agentic development workflow in Claude Code. An orchestrator runs specialized agents (planner, test-writer, implementer, test-auditor, reviewer, documenter) on small specs. Each agent works inside fixed boundaries. Tests, checks and a reviewer decide whether a change is accepted.

The case study is a small payments API in .NET 10: create a payment with an alternative payment method (iDEAL or Klarna), receive a redirect URL, and receive signed provider webhooks that change the payment status. The API is a learning and testing project. It is **not production-ready**.

## Status

| Spec | Description | Status |
|---|---|---|
| [001-create-payment](specs/001-create-payment.md) | `POST /payments`: amount, currency, method. Returns paymentId, redirectUrl, status Pending. | Completed |
| [004-layered-structure](specs/004-layered-structure.md) | Domain, Application, Infrastructure and Api projects. FluentValidation. | Completed |
| [005-typed-payment-contract](specs/005-typed-payment-contract.md) | Typed, non-nullable request and response. Case-insensitive enums. Field errors for bad JSON values. | Completed |
| [006-apply-style-rules](specs/006-apply-style-rules.md) | C# 14 style rules, enforced by analyzers in the build. | Completed |
| [002-webhook](specs/002-webhook.md) | `POST /webhooks/provider` with HMAC-SHA256 signature check and idempotent event processing. Sets the status to `Succeeded` or `Failed`. | Completed |
| [003-out-of-order](specs/003-out-of-order.md) | Webhook events out of sequence. A terminal status is not replaced. | **Planned. Not implemented.** Waits for policy decisions (see the spec). |

Spec 003 has no code. A webhook event can change any status, also `Succeeded` or `Failed` (see [Known limitations](#known-limitations)).

## Prerequisites

- .NET SDK 10 (`global.json` selects a 10.0 SDK).
- Docker. The integration tests start PostgreSQL with Testcontainers. Running the API locally also needs PostgreSQL.
- Git Bash on Windows (or any bash) for the scripts in `scripts/`.
- Claude Code, only if you want to run the agent workflow.

## Setup

```bash
git clone https://github.com/StiliyanM/agentic-dotnet-workflow.git
cd agentic-dotnet-workflow
dotnet restore AgenticPayments.slnx
dotnet build AgenticPayments.slnx
```

The GitHub repository was renamed from `apm-playground` to `agentic-dotnet-workflow` (see [Names](#names)).

## Run the API locally

Start PostgreSQL in Docker. This example uses port 5433 to avoid a conflict with a local PostgreSQL on 5432:

```bash
docker run -d --name agentic-payments-db -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=apm -p 5433:5432 postgres:17-alpine
```

The API needs two settings:
- `ConnectionStrings__Postgres`: the connection string for that container.
- `Webhooks__Provider__Secret`: the secret for the webhook signature. No committed file contains it. Choose your own value. The API does not start without it (`Webhooks:Provider:Secret is required.`).

Run the API (bash):

```bash
ConnectionStrings__Postgres="Host=localhost;Port=5433;Database=apm;Username=postgres;Password=postgres" \
Webhooks__Provider__Secret="local-dev-webhook-secret" \
dotnet run --project src/AgenticPayments.Api --launch-profile http
```

In PowerShell, set the variables first:

```powershell
$env:ConnectionStrings__Postgres = "Host=localhost;Port=5433;Database=apm;Username=postgres;Password=postgres"
$env:Webhooks__Provider__Secret = "local-dev-webhook-secret"
dotnet run --project src/AgenticPayments.Api --launch-profile http
```

The API listens on `http://localhost:5034`. It creates the database schema at startup (`EnsureCreated`). `GET /health` returns 200 when the database is reachable.

**Database created before spec 002.** `EnsureCreated` does not add tables to a database that already has tables. A local database from before spec 002 has no `ProcessedWebhookEvents` table, and webhook requests fail. Remove the container (this deletes its payments) and start a new one with the `docker run` command above.

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

### Webhook example

Sign the raw body with HMAC-SHA256 and the configured secret, and send it in the header `X-Provider-Signature: sha256=<hex>` (bash, needs `openssl`). Replace `<paymentId>` with the `paymentId` from the response above:

```bash
SECRET='local-dev-webhook-secret'
BODY='{"eventId":"evt_001","paymentId":"<paymentId>","status":"succeeded"}'
SIGNATURE=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | sed 's/^.* //')
curl -i -X POST http://localhost:5034/webhooks/provider -H "Content-Type: application/json" -H "X-Provider-Signature: sha256=$SIGNATURE" --data-binary "$BODY"
```

This command was not run as part of the verification. The integration tests send requests in the same format.

Response: `200 OK` with no body. The payment status is now `Succeeded`. The same `eventId` again returns `200` and changes nothing. A missing or wrong signature returns `401`. An unknown `paymentId` returns `404`.

The signature rules, the check order and all responses are in [docs/user/provider-webhook.md](docs/user/provider-webhook.md).

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
- After the test-writer corrects tests, the gates run again. The implementer runs only when production code must change. When the gates pass, the run continues with the test-auditor or the reviewer.
- Documentation-check findings go back to the documenter.
- A maximum of 3 loops. After that, the run stops and the work is committed as WIP on the branch, not merged.
- The orchestrator does not ask questions during a run. It records decisions on unclear specs.

## Execution evidence

- [runs/log.md](runs/log.md): one entry for each run, with loops, gate findings, decisions and status.
- `runs/<id>/evidence.md`: base commit, each step, each verification command with its exit status, and links to the agent reports and test output. The first run with this format is 002: [runs/002-webhook/evidence.md](runs/002-webhook/evidence.md). Its last verification (step 11-verify) ran build, format, unit tests (36/36) and integration tests (55/55) with exit status 0. Earlier runs (001, 004, 005, 006) have only their `runs/log.md` entries.
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
- **No status transition rules.** Each new webhook `eventId` sets the status. A later event can change `Succeeded` to `Failed`, or `Failed` to `Succeeded`. Spec 003 is planned to add rules.
- **Different webhook events at the same time.** When two events with different `eventId` values for the same payment arrive at the same time, the last one saved wins. There is no concurrency check.
- **No read endpoint.** There is no `GET /payments/{id}`. The `201` response has no `Location` header. To see a status change, query the database.
- **Create is not idempotent.** A repeated `POST /payments` creates a second payment. There is no idempotency key.
- **Schema with `EnsureCreated`.** No migrations. When the database already has tables, `EnsureCreated` does nothing. It does not add new tables or columns to an existing schema. A database created before spec 002 must be removed once (see [Run the API locally](#run-the-api-locally)).
- **`amount` accepts a numeric string.** `"amount": "10.50"` is accepted, but `currency` and `method` reject numeric strings. This comes from the JSON Web defaults. No spec decided it, and no test covers it.
- **No authentication, authorization or rate limiting.** The webhook signature is the only check, and only on `POST /webhooks/provider`.
- **Currencies and methods are fixed.** EUR, GBP, USD; `ideal`, `klarna`. There is no check of which currencies a method supports.

## Names

The project had other names before the rename in commit `65b0c4d` (2026-10-01). Historical specs, plans and run-log entries use the old names.

| Item | Old name | New name |
|---|---|---|
| GitHub repository | `apm-playground` | `agentic-dotnet-workflow` |
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
