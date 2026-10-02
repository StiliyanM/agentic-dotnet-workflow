I fixed all three audit findings, in `tests/` only, and committed nothing. The build passes and the affected tests pass.

**Changes:**
1. `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\ProviderWebhookTests.cs`, `RecordAsync_PaymentNotTracked_ThrowsInvalidOperationException` (rule finding 1): the test now captures the exception with `Record.ExceptionAsync(...)` inside the scope. `Assert.IsType<InvalidOperationException>(exception)` is now in the same `Assert.Multiple` as the payment-status (`Pending`) and event-count (`0`) checks.
2. `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Webhooks\ProcessProviderWebhookUseCaseTests.cs`, `ExecuteAsync_RecordReturnsPaymentChangedEveryTime_ThrowsInvalidOperationException` (rule finding 2): same fix. The type check is now in the same `Assert.Multiple` as the `RecordAttempts == MaxAttempts` and `Recorded` empty checks.
3. Same file, `ExecuteAsync_RecordReturnsPaymentChanged_LoadsAgainAndIgnoresEvent` (optional item): I removed `Assert.Equal(PaymentStatus.Succeeded, reloaded.Status)`, because that assertion could not fail.

No other test files or test names changed.

**Verification:**
- `dotnet format whitespace --folder tests` ran.
- `dotnet build AgenticPayments.slnx` succeeded with 0 warnings and 0 errors.
- Unit tests filtered to `ProcessProviderWebhookUseCaseTests`: 16 passed, 0 failed.
- Integration test filtered to `RecordAsync_PaymentNotTracked_ThrowsInvalidOperationException` (Testcontainers PostgreSQL): 1 passed, 0 failed.
- I did not run the full suite or `scripts/verify.sh`.

**PLAN GAP:** none.
**PLAN UPDATE NEEDED:** none.
