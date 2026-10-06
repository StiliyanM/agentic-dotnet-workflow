VERDICT: APPROVE

I found no required changes. I checked each statement in the docs diff against the spec and the current code: `PaymentEndpoints.cs`, `JsonRequestBody.cs`, `CreatePaymentUseCase.cs`, `IdempotencyKeyValidator.cs`, `CreatePaymentRequestHash.cs`, `IdempotencyRecord.cs`, `IdempotencyRecordConfiguration.cs`, `IdempotencyRecordRepository.cs`, `DatabaseInitializer.cs` and the tests. The checks were accuracy, status, check claim and limits.

**Accuracy (all match the code):**
- The header name `Idempotency-Key` is right. The key must be 1 to 100 characters (`IdempotencyRecord.MaxKeyLength`). A key that is empty or only whitespace is rejected (FluentValidation `NotEmpty`). Both 400 messages match `IdempotencyKeyValidator.cs`. The error key is `Idempotency-Key`.
- The 422 and 409 `detail` texts match `PaymentEndpoints.cs` lines 40 and 43 exactly.
- The body is read before the key is checked (`PaymentEndpoints.cs` lines 23-30). A missing field or an unreadable value returns the body error at once, with no key error. Field rule errors are combined with key errors. The test `CreatePayment_MissingKeyAndInvalidField_Returns400WithBothErrors` covers this.
- 24-hour expiry: a key is expired when `now >= CreatedAt + 24h`, so "less than 24 hours" and "24 hours or more" are both right. On renewal the record points to the new payment and the old payment stays.
- The hash is SHA-256 of `amount:F2|currency|method`, so `10.5` and `10.50`, letter case and JSON formatting do not make a different request. The key comparison is case-sensitive (PK on varchar), and a test covers this (`CreatePayment_KeysDifferOnlyInCase_CreatesTwoPayments`).
- The payment and the record are saved in one `SaveChanges`. A concurrent first use gives 409 through a unique violation, and a concurrent renewal gives 409 through the xmin token. Both are described.
- A replay always has `status` `Pending`, as in the comment at `CreatePaymentUseCase.cs` line 37.
- When the header is sent more than one time, `StringValues.ToString()` joins the values with ",". The docs say this and say that no test covers it.
- The schema statements (`CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`) match `DatabaseInitializer.cs`.
- The test names in the docs exist: `InitializeAsync_IdempotencyTableMissing_CreatesTable` in `DatabaseInitializerTests.cs` line 87, and `IdempotencyKeyValidatorTests.cs`.

**Status:** spec 007 is listed as Completed. Cleanup of expired keys is described as missing, not as planned or available. No planned behavior is described as available.

**Limits:** the docs make no claim of production readiness. Known limits are listed: no cleanup job, payments created before spec 007 have no stored key, a replay always shows `Pending`, no migrations.

**Check claims for the orchestrator to compare with run output** (the statement has links, so it is not a CHANGES item):
1. `C:\Users\Stiliyan\source\repos\ApmPlayground\README.md` line 161: "For 007: ... Its last verification (step 07-verify, output in runs/007-create-payment-idempotency/verify/07-verify/summary.txt) ran build, format, unit tests (87/87, unit.log) and integration tests (90/90, integration.log) with exit status 0." Check these files:
   - `runs/007-create-payment-idempotency/evidence.md`
   - `runs/007-create-payment-idempotency/verify/07-verify/summary.txt`
   - `runs/007-create-payment-idempotency/verify/07-verify/unit.log`
   - `runs/007-create-payment-idempotency/verify/07-verify/integration.log`

   Confirm that step 07-verify is the last verification, that the counts are 87/87 and 90/90, and that the exit status is 0.
2. `C:\Users\Stiliyan\source\repos\ApmPlayground\docs\user\provider-webhook.md` (webhook example section): "The example was not run as part of the verification." This line was there before this diff. It claims nothing passed, so there is nothing to compare.

## Optional
- `C:\Users\Stiliyan\source\repos\ApmPlayground\README.md` line 188 (accuracy/readability): "The other gets `409` and must retry, When the first request..." is two sentences joined by a comma. Change ", When" to ". When". The last sentence, "Payments created before spec 007 have no key and cannot be replayed.", is not about two requests at the same time. Move it to its own bullet or to the "Idempotency keys are kept for 24 hours" bullet.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\docs\user\payments-api.md`, the 409 row in the error table: the text "Another request with the same key was saved first, at the same time" covers only the first use of a key. A 409 also comes when two requests renew the same expired key at the same time. A later paragraph says this, so this is only a completeness suggestion.
