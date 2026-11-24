Loop 2 test-writer report for spec 008-concurrent-webhook-mismatch-warning

I fixed the required item. I did not apply the two optional items, and the reasons are below. I changed only tests/ and did not commit.

Required (style), fixed:
- C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\Infrastructure\RacingWebhookEventRepository.cs: `RecordAsync` now has an expression body (`=> coordinator.RecordInOrderAsync(...)`), the same form as `FindAsync`. Its behaviour is unchanged.

Optional 1, not applied:
- `ExecuteAsync_RecordReturnsDuplicateEventStoredPayloadDiffers_ReturnsDuplicatePayloadMismatch` in C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Webhooks\ProcessProviderWebhookUseCaseTests.cs copies the pattern of the theory already on `main` in the same file, `ExecuteAsync_KnownEventIdDifferentPayload_ReturnsDuplicatePayloadMismatchAndChangesNothing` (around line 88). That theory uses the same string labels and the same `ArgumentOutOfRangeException` default.
- Changing only the new test would make the two matching cases in the file look different. Changing the spec 003 test too would rewrite a test that this spec does not cover.
- The default branch cannot run, because both labels are listed in `InlineData`.

Optional 2, not applied:
- `WithRace` has one caller that does not read logs: the spec 003 race theory at C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.IntegrationTests\ProviderWebhookTests.cs:494.
- A second overload, or an optional parameter, would add more code than the one unused `new CapturingLoggerProvider()` argument it removes. The argument does not change what that test checks.

Verification:
- `dotnet format whitespace --folder tests`: no changes left.
- `dotnet build AgenticPayments.slnx -warnaserror`: 0 errors.
- `dotnet format AgenticPayments.slnx --verify-no-changes`: exit 0.
- `dotnet test tests/AgenticPayments.IntegrationTests --filter FullyQualifiedName~ProviderWebhookTests`: 45 passed, 0 failed. This runs all the code that uses the changed decorator.
- I did not re-run the unit tests because no unit test file changed in this loop.

Test names added or changed in this loop: none. The only edit is the expression body in RacingWebhookEventRepository.cs, which is test infrastructure.

PLAN GAP: none.
PLAN UPDATE NEEDED: none.
