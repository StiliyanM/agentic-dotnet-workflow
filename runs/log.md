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

## 004-layered-structure (2026-10-01)

- **Status:** PASSED. Committed on `spec/004-layered-structure` and merged into `main`.
- **Loops:** 1
- **Final result:** 30 unit tests and 12 integration tests pass. Build has 0 warnings. Format check passes.
- **Before the run:** the standing rules were added on `main` (`docs/architecture.md`, agent prompt changes, specs 004 and 005), because you asked for the layers, FluentValidation and typed contracts.

### Problems that each gate found

| Gate | Loop 0 | Loop 1 |
|---|---|---|
| Preflight | None | – |
| Boundary checks | None | None |
| Build / format / test | None (40 of 40 pass) | None (42 of 42 pass) |
| Test-auditor | PASS | PASS |
| Reviewer | CHANGES: FluentValidation `NotEmpty()` treats `" "` as missing, so the messages for currency and method changed from spec 001. Sent to the test-writer (new `" "` cases), then the implementer. | APPROVE (2 optional items) |

### Decisions on unclear specs

- Integration tests: only the `using` lines changed, because the types moved to new namespaces. Test bodies did not change.
- Validator keys: `OverridePropertyName` keeps the error keys camelCase. There is no global resolver.
- Validation runs in `CreatePaymentUseCase`. The endpoint only maps the result to HTTP.
- `SupportedCurrencies` is in Domain. `PaymentMethodNames` is in Application, because it maps wire strings.
- `IPaymentRepository.AddAsync` adds and saves in one call. There is no Unit of Work.
- Required-string check: `IsNullOrEmpty`, not `NotEmpty()`, to keep the spec 001 behavior.

### Open items (optional, not done)

- `CreatePaymentUseCase` parses the method a second time to get the enum. Spec 005 (enum contracts) should remove this.

## 005-typed-payment-contract (2026-10-01)

- **Status:** PASSED. Committed on `spec/005-typed-payment-contract` and merged into `main`.
- **Loops:** 2
- **Final result:** 19 unit tests and 31 integration tests pass. Build has 0 warnings. Format check passes.

### Problems that each gate found

| Gate | Loop 0 | Loop 1 | Loop 2 |
|---|---|---|---|
| Preflight | None | – | – |
| Boundary checks | None | None | None |
| Build / format / test | None (45 of 45 pass) | None (45 of 45 pass) | None (50 of 50 pass) |
| Test-auditor | FAIL: the valid filler data in the raw-JSON integration tests was hard-coded, not from AutoFixture. Sent to the test-writer, with the auditor's note to also check the saved currency. | PASS | PASS |
| Reviewer | Not run | CHANGES: (1) undefined enum values reach the use case, which gives a saved row and then a 500; (2) the redirect URL is built after the save. Sent to the test-writer, then the implementer. The new tests showed that `"ideal, klarna"` binds to `Klarna`. | APPROVE (3 optional items) |

### Decisions on unclear specs

- `Currency { EUR, GBP, USD }` is in Domain and stored as the ISO string. `SupportedCurrencies` and `PaymentMethodNames` are deleted.
- JSON enums: the strict converter in Api reads one member name in any case and writes the member name. Status stays "Pending".
- Binding errors: `ThrowOnBadRequest` with `RequestBodyExceptionHandler` in Api. A value that cannot be converted gives "X has an invalid value.". A missing field gives "X is required.". Malformed JSON and an empty body give a plain 400 problem.
- The validator has the amount rules and `IsInEnum`. The rules for null and unknown strings are gone, because the types now prevent those values.
- Response: `Guid`, `Uri`, `PaymentStatus`. The redirect segment comes from an explicit switch (lowercase), not from `ToLowerInvariant()` (CA1308).
- Orchestrator: like in spec 001, the reviewer's code findings needed tests, so loop 2 started at the test-writer.

### Open items (optional, not done)

- Missing-field detection reads the System.Text.Json message text. The integration tests catch a change in a later .NET version.
- `StrictEnumConverter` could cache the name lookup.
