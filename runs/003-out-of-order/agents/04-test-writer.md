Loop 1 test update for spec 003-out-of-order is done. I made one change, and I made it under `tests/` only. I did not commit.

**What changed**
- File: `C:\Users\Stiliyan\source\repos\ApmPlayground\tests\AgenticPayments.UnitTests\Webhooks\ProcessProviderWebhookUseCaseTests.cs`
- Test: `ExecuteAsync_KnownEventIdWithoutHash_ReturnsDuplicate` (Theory: Succeeded, Failed)
- The seeded row is now `new ProcessedWebhookEvent(request.EventId, payment.Id, null)` instead of `null!`.
- I replaced the two-line comment that said "the constructor parameter is not nullable" with: `// A row stored before spec 003 has no hash.`

**What I left alone**
- I searched `tests/` for other `null!` uses: there are none. All other constructor calls pass `WebhookPayloadHash.Compute(...)`, which matches the updated signature `ProcessedWebhookEvent(string eventId, Guid paymentId, string? payloadHash)`.
- The C1 concurrency test and its decorators (`RacingPaymentRepository`, `RacingWebhookEventRepository`, `WebhookRaceCoordinator`) are unchanged. The new FLAGGED decision keeps that design and test as they are.
- The integration test for rows without a hash still makes such a row with raw SQL, so the plan change does not affect it.

**Formatting and build**
- `dotnet format whitespace --folder tests` finished with exit code 0.
- I did not run the build. It is still expected to fail because the production types do not exist yet.

PLAN GAP: none.
PLAN UPDATE NEEDED: none.
