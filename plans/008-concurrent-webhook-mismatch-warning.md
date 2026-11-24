# Plan 008-concurrent-webhook-mismatch-warning

Bug fix. When a request loses the race for an `eventId` (its save fails on the unique key), the use case reads the stored event again and compares the payload hash. No new public type, no schema change, no new response. The tests compile against the current code, so the before-fix step can run them.

## 1. Files

| File | Project | Change |
|---|---|---|
| `src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs` | Application | On `WebhookRecordResult.DuplicateEvent`, read the stored event again with `webhookEvents.FindAsync` and return `Duplicate` or `DuplicatePayloadMismatch` from the hash compare. Move the existing compare into one private static method that both paths use. |
| `tests/AgenticPayments.UnitTests/Webhooks/ProcessProviderWebhookUseCaseTests.cs` | UnitTests | Add the unit tests in section 3. Keep `ExecuteAsync_RecordReturnsDuplicateEvent_ReturnsDuplicate` unchanged (it now covers "no stored event found after the unique-key failure"). |
| `tests/AgenticPayments.IntegrationTests/Infrastructure/WebhookRaceCoordinator.cs` | IntegrationTests | Select R1 with a predicate on the event (not by `eventId` only); the first event that matches and claims the slot saves first. |
| `tests/AgenticPayments.IntegrationTests/Infrastructure/RacingWebhookEventRepository.cs` | IntegrationTests | Pass the whole `ProcessedWebhookEvent` to the coordinator. |
| `tests/AgenticPayments.IntegrationTests/ProviderWebhookTests.cs` | IntegrationTests | Add the C3-C6 tests. Change `Webhook_ConcurrentTerminalEvents_KeepFirstSavedStatusAndIgnoreOther` to the new coordinator constructor (`e => e.EventId == firstEventId`). Add a private helper that builds a factory with the racing repositories and the capturing logger. |

No change in `src/AgenticPayments.Api`, `src/AgenticPayments.Infrastructure`, `src/AgenticPayments.Domain`, or `FakeWebhookEventRepository` (its `BeforeResultReturned` hook can already add the winner's event to `Stored`).

## 2. Public types and signatures

No public production type or signature changes. These existing signatures are the ones the tests use:

```csharp
// Application (unchanged)
public sealed class ProcessProviderWebhookUseCase(
    IValidator<ProviderWebhookRequest> validator,
    IPaymentRepository payments,
    IWebhookEventRepository webhookEvents)
{
    public const int MaxAttempts = 2;
    public Task<ProviderWebhookResult> ExecuteAsync(ProviderWebhookRequest request, CancellationToken cancellationToken);
}

public interface IWebhookEventRepository
{
    Task<ProcessedWebhookEvent?> FindAsync(string eventId, CancellationToken cancellationToken);
    Task<WebhookRecordResult> RecordAsync(ProcessedWebhookEvent webhookEvent, Payment payment, CancellationToken cancellationToken);
}

public enum WebhookRecordResult { Recorded, DuplicateEvent, PaymentChanged }          // unchanged
public static class WebhookPayloadHash { public static string Compute(Guid paymentId, ProviderPaymentStatus status); }
// ProviderWebhookOutcome: Processed, Ignored, Duplicate, DuplicatePayloadMismatch, PaymentNotFound, Invalid (unchanged)
```

HTTP (unchanged): `POST /webhooks/provider`, header `X-Provider-Signature`, body `{ "eventId": string, "paymentId": guid, "status": "succeeded" | "failed" }`.
- Processed, Ignored, Duplicate: 200, no body.
- DuplicatePayloadMismatch: 200, no body, one `Warning` in category `AgenticPayments.Api.Webhooks.WebhookEndpoints`: "Webhook event {EventId} was received again with a different payload. It was not processed again."
- 401, 400, 404, 415 as before.

Test helper signatures (tests project, chosen for the test-writer):

```csharp
// Forces two requests to overlap: both load the payment before either saves; the first event that matches isFirst saves first.
public sealed class WebhookRaceCoordinator(Func<ProcessedWebhookEvent, bool> isFirst)
{
    public Task PaymentLoadedAsync(CancellationToken cancellationToken);
    // The first call whose event matches isFirst claims the first slot (atomic claim) and saves at once;
    // every other call waits until that save has returned.
    public Task<WebhookRecordResult> RecordInOrderAsync(
        ProcessedWebhookEvent webhookEvent,
        Func<Task<WebhookRecordResult>> record,
        CancellationToken cancellationToken);
}
```

C3-C5 select R1 by payload hash (`e => e.PayloadHash == WebhookPayloadHash.Compute(p, r1Status)`). C6 has two equal payloads, so it uses `_ => true`: the first request that reaches the save is R1; the other waits. `RacingPaymentRepository` is unchanged.

## 3. Tests

Unit (`ProcessProviderWebhookUseCaseTests`). The fake returns `DuplicateEvent` from `RecordResults`, and `BeforeResultReturned` adds the winner's event to `Stored` when the result is `DuplicateEvent` (so the first `FindAsync` finds nothing and the read after the failure finds the winner).

| Name | Type | Proves |
|---|---|---|
| `ExecuteAsync_RecordReturnsDuplicateEventStoredPayloadDiffers_ReturnsDuplicatePayloadMismatch` (theory: `"other status"`, `"other paymentId"`) | unit | A request that loses the race against a different payload gets `DuplicatePayloadMismatch`, no errors, `RecordAttempts == 1`, `Recorded` empty. Fails before the fix. |
| `ExecuteAsync_RecordReturnsDuplicateEventStoredPayloadSame_ReturnsDuplicate` | unit | A loser with the same payload gets `Duplicate`, `RecordAttempts == 1`, `Recorded` empty. |
| `ExecuteAsync_RecordReturnsDuplicateEvent_ReturnsDuplicate` (existing, unchanged) | unit | If the read after the failure finds no event, the result stays `Duplicate` (no exception). |

Integration (`ProviderWebhookTests`, forced overlap with the racing repositories and `CapturingLoggerProvider` on one factory; both requests sent with `Task.WhenAll`):

| Name | Type | Proves |
|---|---|---|
| `Webhook_ConcurrentSameEventIdDifferentStatus_FirstSavedWinsAndLogsOneWarning` (theory: C3 `"succeeded","failed",Succeeded`; C4 `"failed","succeeded",Failed`) | integration | Both 200 with empty body; P has R1's status; exactly one event row for E with R1's payment id and hash; exactly one mismatch warning whose message contains E (count of warnings in category `WebhookEndpoints` at `Warning` level that contain E equals 1). May pass or fail before the fix (E3). |
| `Webhook_ConcurrentSameEventIdOtherPayment_KeepsOtherPaymentPendingAndLogsOneWarning` (C5) | integration | Both 200 with empty body; P `Succeeded`; Q `Pending`; one event row for E, `PaymentId == P`, hash of R1; exactly one mismatch warning for E. Must fail before the fix. |
| `Webhook_ConcurrentSameEventIdSamePayload_ProcessesOnceAndLogsNoWarning` (C6) | integration | Both 200 with empty body; P `Succeeded`; one event row for E; zero mismatch warnings. Guard, can pass before the fix. |
| `Webhook_ConcurrentTerminalEvents_KeepFirstSavedStatusAndIgnoreOther` (existing, only the coordinator construction changes) | integration | The spec 003 race with two event ids still passes. |
| `Webhook_SameEventIdDifferentPayload_Returns200ChangesNothingAndLogsWarning`, `Webhook_SameEventSamePayload_Returns200AndLogsNoWarning`, `Webhook_EventStoredWithoutPayloadHash_CountsAsDuplicate` (existing, unchanged) | integration | Sequential A4/B4 and the pre-003 row rule still hold. |

## 4. Edge cases

| Edge case | Test |
|---|---|
| Loser saves another payment row (C5): only the unique key fails | `Webhook_ConcurrentSameEventIdOtherPayment_...`; unit theory `"other paymentId"` |
| Same payment, other status, unique-key failure reported | `Webhook_ConcurrentSameEventIdDifferentStatus_...`; unit theory `"other status"` |
| Same payment, other status, `xmin` failure reported first (retry path) | `Webhook_ConcurrentSameEventIdDifferentStatus_...` (whichever failure EF Core reports, the result must be one warning); existing `ExecuteAsync_RecordReturnsPaymentChanged_LoadsAgainAndIgnoresEvent` |
| Same payload in the race (C6) | `Webhook_ConcurrentSameEventIdSamePayload_...`; unit `..._StoredPayloadSame_ReturnsDuplicate` |
| Exactly one warning, from R2 only (E1) | integration C3-C5 count = 1 |
| Loser changes no payment and adds no event row | C3-C5 status and row assertions; unit `Recorded` empty |
| No 500 | all race tests assert 200 for both responses |
| No stored event found after the unique-key failure | existing `ExecuteAsync_RecordReturnsDuplicateEvent_ReturnsDuplicate` |
| Event stored before spec 003 (no hash) | out of scope; existing `Webhook_EventStoredWithoutPayloadHash_CountsAsDuplicate` still passes |

## 5. Pattern

None.

## 6. Data and schema

None. Existing rows: events without a hash (before spec 003) still count as duplicates without a warning; the shared compare method keeps the `PayloadHash is null` rule.

## 7. Atomic operations

- "Record the event and change the status" stays in `WebhookEventRepository.RecordAsync` (one `SaveChangesAsync`, one transaction). Unchanged. It saves the payment that `IPaymentRepository.FindAsync` loaded and the same scoped `AppDbContext` tracks (comment at the call site in the use case, already present).
- The new read after `DuplicateEvent` is a read only (`AsNoTracking`), outside any transaction. After a unique-key failure `RecordAsync` clears the change tracker, so the read sees the committed row of the winner and the loser's changes are gone.

## 8. Public contract decisions

| Decision | Reason |
|---|---|
| A race loser with a different hash returns `DuplicatePayloadMismatch`: 200, no body, the existing warning once. | Required behavior 2; D5 of spec 003; E1 (only R2 logs). |
| A race loser with the same hash returns `Duplicate`: 200, no body, no warning. | Required behavior 3. |
| Warning text, level and category unchanged. | Contract that does not change. |
| If the read after the unique-key failure finds no event, the result is `Duplicate` (200, no warning). | Required behavior 5 (no 500). See FLAGGED F1. |

## 9. Implementation choices

- Fix in the use case, not in the repository: the repository keeps returning `DuplicateEvent`; no new `WebhookRecordResult` value. Smallest change; the hash compare already lives in the use case.
- Explicit read after `DuplicateEvent` instead of `continue` in the retry loop: `continue` could use up `MaxAttempts` after an earlier `PaymentChanged` and throw (500).
- One private static method `ToDuplicateResult(ProcessedWebhookEvent storedEvent, string hash)` (null hash or equal hash gives `Duplicate`, else `DuplicatePayloadMismatch`), used by the first read and by the read after the failure (DRY).
- Endpoint unchanged: it already logs for `DuplicatePayloadMismatch`.
- Test coordinator: predicate plus atomic claim (`Interlocked.CompareExchange`) so that one test helper serves the spec 003 race, C3-C5 (payload) and C6 (equal payloads).

## 10. Flagged decisions (Decisions)

- **E1, E2, E3**: user decisions in the spec; followed (R2 logs; before-fix step runs; C3/C4 may pass before the fix). Not flagged.
- **F1 FLAGGED: no stored event after the unique-key failure.** Decision: return `Duplicate` (200, no warning), the current behavior. It cannot happen with the current code (event rows are never deleted, and the unique violation means a committed row exists). Alternative: throw `InvalidOperationException` (500). Effect of the alternative: an impossible state becomes visible as an error, but it breaks required behavior 5 and the existing unit test `ExecuteAsync_RecordReturnsDuplicateEvent_ReturnsDuplicate`.
- Test design (agent decision, spec leaves it to the test-writer): R1 is selected by payload hash in C3-C5 and by first arrival in C6, where both payloads are equal and the choice does not change the expected result.
