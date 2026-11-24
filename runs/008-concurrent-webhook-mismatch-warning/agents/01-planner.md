I wrote the plan for spec 008 at C:\Users\Stiliyan\source\repos\ApmPlayground\plans\008-concurrent-webhook-mismatch-warning.md.

**The fix.** There is one production change, in `src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs`:
- When `RecordAsync` returns `DuplicateEvent`, the use case reads the stored event again with `webhookEvents.FindAsync` and compares the payload hash.
- A different hash returns `DuplicatePayloadMismatch`. The endpoint already turns that into 200 with the existing warning.
- The same hash returns `Duplicate`.
- The hash compare that already exists moves into one private static method that both paths use. It keeps the rule that a stored event with no hash counts as a duplicate.
- The re-read is written out after `DuplicateEvent`. It does not `continue` the retry loop, because after an earlier `PaymentChanged` that could use up `MaxAttempts` and throw (500).

Nothing else changes: no public type or signature, no schema, nothing in Api, Infrastructure or Domain, and no new `WebhookRecordResult` value. The tests therefore compile against the current code, so the before-fix step can run them.

**Tests.**
- **Unit (`ProcessProviderWebhookUseCaseTests`):**
  - A theory for a lost race against a different payload, with two cases: other status and other paymentId. It expects `DuplicatePayloadMismatch`, 1 record attempt and nothing recorded. It fails before the fix.
  - A test for a lost race with the same payload, which expects `Duplicate`.
  - The existing test `ExecuteAsync_RecordReturnsDuplicateEvent_ReturnsDuplicate` stays as it is and now covers "no stored event found after the failure".
  - The fake needs no change: its `BeforeResultReturned` hook adds the winner's event to `Stored`.
- **Integration (`ProviderWebhookTests`):**
  - C3 and C4 run as one theory. They may pass or fail before the fix (E3).
  - C5 must fail before the fix.
  - C6 is the guard.
  - Each test sets up the racing repositories and the capturing logger on one factory, asserts that both responses are 200 with an empty body, and checks the payment statuses and the single event row for E (its paymentId and hash).
  - Each test counts the mismatch warnings that contain E: exactly 1 for C3–C5 and 0 for C6.
  - The sequential A4/B4 tests and the event-without-hash test stay unchanged.
- **Test helper change (for the test-writer):**
  - The constructor becomes `WebhookRaceCoordinator(Func<ProcessedWebhookEvent, bool> isFirst)`.
  - The record method becomes `RecordInOrderAsync(ProcessedWebhookEvent webhookEvent, Func<Task<WebhookRecordResult>> record, CancellationToken cancellationToken)`. It uses an atomic claim, so only one request can save first.
  - C3–C5 select R1 by payload hash. C6 uses `_ => true`, so the first request to reach the save is R1; the two payloads are the same, so it does not matter which one wins.
  - `RacingWebhookEventRepository` passes the whole event to the coordinator. The existing spec 003 race test changes to `e => e.EventId == firstEventId`.

**Decisions.**
- E1, E2 and E3 are followed: only R2 logs the warning, the before-fix step runs, and C3/C4 do not have to fail before the fix.
- **F1 FLAGGED:** if the re-read after the unique-key failure finds no event, the result is `Duplicate` (200, no warning), which is the current behavior. The current code cannot reach this case, because event rows are never deleted. The alternative is to throw (500), but that breaks required behavior 5 and the existing unit test.
- The way the test selects R1 is an agent decision. The spec leaves it to the test-writer.

No DECISION CONFLICT.
