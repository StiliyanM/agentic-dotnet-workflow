# Run log

## 001-create-payment (2026-10-01)

- **Status:** PASSED. Committed on `spec/001-create-payment` and merged into `main`.
- **Loops:** 2
- **Final result:** 24 unit tests and 12 integration tests pass. Build has 0 warnings. Format check passes.

### Problems that each gate found

| Gate | Loop 0 | Loop 1 | Loop 2 |
|---|---|---|---|
| Preflight | First try failed: the `docker` command was not on PATH (Docker Desktop is installed for this user only). No loop counted. Passed after the PATH fix. | – | – |
| Boundary checks | None | None | None |
| Build / format / test | None (31 of 31 pass) | None (33 of 33 pass) | None (36 of 36 pass) |
| Test-auditor | FAIL: the integration tests always sent "ideal". AutoFixture gives the first enum value in each new test instance, so "klarna" was never tested end to end. Expected values came from production code. Sent to the test-writer. | PASS | PASS |
| Reviewer | Not run | CHANGES: there was no upper limit on the amount, so an amount above the `numeric(18,2)` limit gave a 500 instead of a 400. Sent to the test-writer, then the implementer. | APPROVE (4 optional items) |

### Decisions on unclear specs

- Supported currencies: EUR, GBP and USD, exact uppercase only.
- Method values: "ideal" and "klarna", case-sensitive.
- Amount: `decimal` with at most 2 decimal places, in a `numeric(18,2)` column. The maximum is 9999999999999999.99.
- Response: 201 with no `Location` header, because there is no GET endpoint yet.
- redirectUrl: `https://pay.example.com/{method}/{paymentId}`. It is made from the data and is not stored.
- Errors: 400 as an RFC 7807 validation problem, with keys `amount`, `currency` and `method`.
- Schema: `EnsureCreated()` at startup, with no migrations.
- Orchestrator: the loop 2 reviewer finding needed new tests, but rule 3 sends code problems to the implementer, which cannot write tests. So the loop started at the test-writer (tests first) and then went to the implementer.

### Open items (optional, not done)

- `PaymentEndpoints` parses the method a second time after validation.
- `SupportedCurrencies.All` could be private.
- `EnsureCreated()` cannot add tables to a database that already exists. Spec 002 may need migrations.
- `POST /payments` is not idempotent, so a retried request makes a new payment. No spec asks for this.
