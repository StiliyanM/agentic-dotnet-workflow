VERDICT: CHANGES

Documentation review of .agent-input/007-create-payment-idempotency/docs.diff (README.md, docs/user/payments-api.md, docs/user/provider-webhook.md). I checked it against specs/007-create-payment-idempotency.md and the current code in src/ and tests/.

Items

1. C:\Users\Stiliyan\source\repos\ApmPlayground\docs\user\payments-api.md:75 (accuracy)
   Problem: The paragraph lists the cases where a 400 has no `Idempotency-Key` error as "415, a body that is not a JSON object, or a field value that cannot be read". This list is incomplete. In `JsonRequestBody.Read` (src/AgenticPayments.Api/JsonRequestBody.cs:56-81), a missing required member also returns early, before `PaymentEndpoints.CreatePaymentAsync` reads the header (src/AgenticPayments.Api/Payments/PaymentEndpoints.cs:27-33). An empty body or invalid JSON returns early too (JsonRequestBody.cs:33-36). For example, `{"currency":"EUR","method":"ideal"}` with no header returns 400 with only `errors.amount` and no `Idempotency-Key` error. The next sentence ("When the body can be read, the key errors and the field rule errors ... are in one validation problem") then suggests that this request also gets the key error, which is wrong.
   Change: Make the list complete, for example: "When the body cannot be read or has a missing field (415, an empty body, invalid JSON, a body that is not a JSON object, a missing `amount`/`currency`/`method`, or a field value that cannot be read), the response has no `Idempotency-Key` error, even when the header is missing. Only the field rule errors (for example `"Amount must be greater than 0."`) are combined with the key errors in one validation problem."

Check claims, for the orchestrator to compare with the evidence
My agent definition says I do not read `runs/`. So I did not open runs/007-create-payment-idempotency/verify/, although your request asked me to. Please compare this statement with the files yourself:
- C:\Users\Stiliyan\source\repos\ApmPlayground\README.md:161 says: "For 007: runs/007-create-payment-idempotency/evidence.md. Its last verification (step 07-verify, output in runs/007-create-payment-idempotency/verify/07-verify/summary.txt) ran build, format, unit tests (87/87, unit.log) and integration tests (90/90, integration.log) with exit status 0." It has links, so it is not an unlinked-claim finding. Confirm these against `verify/07-verify/summary.txt`, `unit.log` and `integration.log`: step 07-verify is the last verification, the counts are 87/87 and 90/90, and the exit status is 0. Also confirm that each linked file is tracked.
- I found no other statement in the diff that says a check passed. provider-webhook.md says that the example "was not run as part of the verification", which is correct.

Checked and correct (no change needed)
- Accuracy:
  - Header required: a missing, empty or whitespace-only header gives the message "Idempotency-Key header is required.", and more than 100 characters gives "...must be at most 100 characters." (IdempotencyKeyValidator.cs, which uses FluentValidation NotEmpty). The error key `Idempotency-Key` is right.
  - The 422 and 409 `detail` texts match PaymentEndpoints.cs:40,43.
  - Replay: same paymentId and redirectUrl, and status always Pending (CreatePaymentUseCase.cs:37-40; test CreatePayment_ReplayAfterStatusChange_ReturnsFirstResponseWithPending).
  - The hash covers amount (F2), currency and method, so case, scale, property order and whitespace do not make a different request (CreatePaymentRequestHash.cs; the variant theory test).
  - Keys are case-sensitive (test CreatePayment_KeysDifferOnlyInCase_CreatesTwoPayments).
  - 24h expiry with `>=`, renewal that keeps the old payment, a 400 that stores no key, and one transaction (IdempotencyRecord.cs, IdempotencyRecordRepository.cs).
  - A concurrent renewal gets 409 through the xmin token (test SaveAsync_ExpiredRecordRenewedByOtherScope_ReturnsKeyConflictAndSavesNothing).
  - The CREATE TABLE/INDEX IF NOT EXISTS statements are in DatabaseInitializer.cs, and the listed test names exist in DatabaseInitializerTests.cs and IdempotencyKeyValidatorTests.cs.
  - Repeated headers are joined with "," (StringValues.ToString in PaymentEndpoints.cs:33), and the doc says that no test covers this.
- Status: 007 is marked Completed after the approved code review. The cleanup job and the read endpoint are listed only as limits.
- Limits: there is no claim of production readiness. The new limits (no cleanup, 409 on a race, Pending on replay, no key for payments from before 007) match the code.

Optional
- C:\Users\Stiliyan\source\repos\ApmPlayground\README.md:188 (accuracy): "One gets `201`. The other gets `409`" is true only when both requests look up the key before either one saves. When the second lookup comes after the first save, the second request gets a 201 replay. The spec (C1) also allows "201 with the same `paymentId` or 409". You could write "The other gets `409` (or the replay, if the first request was already saved)", in line with payments-api.md, which says "The request whose save comes second gets `409`".
