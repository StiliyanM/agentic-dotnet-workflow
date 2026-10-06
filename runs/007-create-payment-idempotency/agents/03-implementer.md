Spec 007-create-payment-idempotency is implemented and all checks pass. I changed only files in `src/`. I did not change any tests, did not change `AgenticPayments.slnx` and did not commit.

**Test result**
- `bash scripts/verify.sh build unit integration` passed:
  - build: 0 warnings, 0 errors.
  - unit tests: 88 of 88 passed, 0 skipped.
  - integration tests (Docker): 93 of 93 passed, 0 skipped.
- `bash scripts/verify.sh format` passed.

**Test problems:** none. I found no wrong test.

**Files I added**
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Domain\Payments\IdempotencyRecord.cs`
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Application\Payments\IdempotencyKeyValidator.cs`
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Application\Payments\CreatePaymentRequestHash.cs`
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Application\Payments\IIdempotencyRecordRepository.cs`
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Application\Payments\IdempotencySaveResult.cs`
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Application\Payments\CreatePaymentOutcome.cs`
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Infrastructure\Persistence\IdempotencyRecordConfiguration.cs`
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Infrastructure\Persistence\IdempotencyRecordRepository.cs`

**Files I changed**
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Application\Payments\CreatePaymentResult.cs`: adds `Outcome` and the factories `IdempotencyKeyReused()` and `IdempotencyKeyInProgress()`.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Application\Payments\CreatePaymentUseCase.cs`: new constructor, and `ExecuteAsync(idempotencyKey, request, ct)` with the order from the plan.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Application\Payments\IPaymentRepository.cs`: `AddAsync` removed.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Application\ApplicationServiceCollectionExtensions.cs`: registers `TimeProvider.System` as a singleton.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Infrastructure\InfrastructureServiceCollectionExtensions.cs`: registers `IIdempotencyRecordRepository` (scoped).
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Infrastructure\Persistence\AppDbContext.cs`: adds `DbSet<IdempotencyRecord> IdempotencyRecords`.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Infrastructure\Persistence\PaymentRepository.cs`: `AddAsync` removed.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Infrastructure\Persistence\DatabaseInitializer.cs`: idempotent `CREATE TABLE IF NOT EXISTS` and `CREATE INDEX IF NOT EXISTS` for `IdempotencyRecords`.
- `C:\Users\Stiliyan\source\repos\ApmPlayground\src\AgenticPayments.Api\Payments\PaymentEndpoints.cs`: adds the `IdempotencyKeyHeader` const. The endpoint reads the header after the body and maps the four outcomes: 201, 400 validation problem, 422 problem and 409 problem, with the detail texts from the plan.

**Small differences from the plan**
- `CreatePaymentResult.IsSuccess` is `Response is not null`, not `Outcome == Success` as the plan says. The result is the same, because only `Success` sets a response. I did it so the null-check attribute on `Response` works.
- The `xmin` concurrency token on `IdempotencyRecord` is a shadow property named `Version`, as on `Payment`.
