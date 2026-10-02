I updated C:\Users\Stiliyan\source\repos\ApmPlayground\plans\003-out-of-order.md and no other file. It covers both test-writer items. Item 1: the constructor now accepts a null hash. Item 2: the design and the tests stay as they are, and the reasons are recorded as a new FLAGGED decision.

**Item 1: the constructor now accepts a null hash (PLAN UPDATE NEEDED)**
- The constructor parameter is now `string? payloadHash`, with no null check:
  `public sealed class ProcessedWebhookEvent(string eventId, Guid paymentId, string? payloadHash)`
  A comment says that null means "stored before spec 003", and that production code always passes `WebhookPayloadHash.Compute(...)`.
- Section 1 (Files): the `ProcessedWebhookEvent.cs` line says "nullable constructor parameter (`string? payloadHash`, no null guard)".
- Section 2 (Domain signatures): the signature above, plus the comment.
- Section 3 (Tests): `ExecuteAsync_KnownEventIdWithoutHash_ReturnsDuplicate` seeds `new ProcessedWebhookEvent(eventId, paymentId, null)`. The existing `null!` still compiles and works, so the test-writer does not have to change it.
- Section 6 (Data and schema): rows from before this spec load with `PayloadHash == null`, which the nullable parameter allows.
- Section 8 (Public contract): the stored hash is NULL only for rows from before this spec.
- Section 9 (Implementation choices): explains why the parameter is nullable and has no null check.
- Section 10 (Decisions): a new entry, "Nullable hash in the Domain constructor". It is not FLAGGED because a client or the database cannot see it; the column was already nullable.

**Item 2: the C1 requirement "inside their own transaction"**
- Decision: neither the design nor the tests change. This is recorded as a new `FLAGGED` entry in section 10. The reasons:
  1. Each request has its own scoped `AppDbContext` and its own connection. Its read of the payment runs in its own implicit PostgreSQL transaction, and the two requests never share a transaction.
  2. The point of the requirement is that both requests read the payment before either one saves. The C1 test forces exactly that with its test wrappers around the repositories.
  3. The test can fail: without the `xmin` concurrency token, the second save overwrites the first status.
  4. An explicit READ COMMITTED transaction from the read to the save would not change the result. The UPDATE still checks `xmin`. It would only add a transaction interface in Application and more code.
- Alternative and its effect (also in the entry): an explicit transaction per request, either a unit-of-work interface or REPEATABLE READ. The test would then match the spec's wording exactly, but there is more code. With REPEATABLE READ, `RecordAsync` would also have to handle PostgreSQL error 40001 (serialization failure). Public behavior is the same either way.
- Related text added elsewhere:
  - Section 3: the `Webhook_ConcurrentTerminalEvents_...` test says that each request reads in its own implicit transaction, and points to the decision.
  - Section 7, operation 2: now says that the read and the save each run in their own implicit transaction, and that the `xmin` check gives the same result as one transaction around both.
  - Section 9: a new line says there is no explicit `BeginTransaction` and no transaction interface in Application.

Nothing else changed: no other signatures, tests, routes or status codes.
