I wrote the plan to `C:\Users\Stiliyan\source\repos\ApmPlayground\plans\007-create-payment-idempotency.md` and changed no other file.

**Design:**
- **Storage:** a new Domain entity `IdempotencyRecord` (`Key`, `RequestHash`, `PaymentId`, `CreatedAt`) goes in table `IdempotencyRecords`. `Key` is the primary key (max 100, case-sensitive), with an FK to `Payments` (cascade delete) and an `xmin` concurrency token.
- **One owner for the save:** a new port `IIdempotencyRecordRepository` has `FindAsync(key)` and `SaveAsync(record, payment)`. `SaveAsync` stores the payment and the record (insert, or renewal of an expired key) in one `SaveChanges`. It returns `Saved` or `KeyConflict`.
- **Removal:** `IPaymentRepository.AddAsync` is removed because nothing calls it any more.
- **Use case:** the new signature is `CreatePaymentUseCase.ExecuteAsync(string idempotencyKey, CreatePaymentRequest request, CancellationToken)`. Its dependencies are `IValidator<CreatePaymentRequest>`, `IValidator<string>` (the new `IdempotencyKeyValidator`), `IIdempotencyRecordRepository` and `TimeProvider`.
- **Outcomes:** `CreatePaymentResult` gets an `Outcome` with the values `Success`, `Invalid`, `IdempotencyKeyReused` (422) and `IdempotencyKeyInProgress` (409).
- **Request hash:** `CreatePaymentRequestHash.Compute(amount, currency, method)` is SHA-256 of `"{amount:F2}|{Currency}|{Method}"`.
- **Existing databases:** `DatabaseInitializer` gets an idempotent `CREATE TABLE IF NOT EXISTS` and `CREATE INDEX IF NOT EXISTS`.

**Notes for the test-writer:**
- All existing `CreatePaymentTests` must now send a unique `Idempotency-Key`. Without it they get 400.
- `FakePaymentRepository` and `RacingPaymentRepository` lose `AddAsync`.
- The C1 test uses a test decorator over `IIdempotencyRecordRepository`, like the spec 003 race test.

**FLAGGED decisions (please record these):**
1. A request that overlaps another one with the same key gets 409 with no retry, because the conflict is detected at the save. C1 then gives 201 + 409. The alternative is to read the record again and replay.
2. There is no separate "processing" state: the record and the payment are stored in one transaction, so no "processing" row is ever visible.
3. A replay always returns `status: "Pending"` (the first response), even after a webhook changed the payment.
4. The 400 validation problem uses the error key `Idempotency-Key` (the header name), not a camelCase field name.
5. Body read errors (415, malformed JSON, binding errors) come before the key check. Key errors and field validation errors are returned together in one 400.
6. A key with only whitespace is rejected with 400.
7. A key is expired at exactly 24h (`>=`).
8. If the header is sent more than once, its values are joined with `,` and used as one key.
9. The schema upgrade uses raw idempotent SQL, not EF Core migrations.
