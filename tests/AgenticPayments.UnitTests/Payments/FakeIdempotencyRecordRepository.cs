using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;

namespace AgenticPayments.UnitTests.Payments;

public sealed class FakeIdempotencyRecordRepository : IIdempotencyRecordRepository
{
    // Records that were stored before the test, by key (ordinal, so case-sensitive like the database key).
    public Dictionary<string, IdempotencyRecord> Stored { get; } = [];

    // The records that SaveAsync stored (result Saved), with the payment that was stored with them.
    public List<(IdempotencyRecord Record, Payment Payment)> Saved { get; } = [];

    // The result of the next SaveAsync calls, in order. When it is empty, SaveAsync stores and returns Saved.
    // KeyConflict simulates a concurrent request that stored or renewed the same key first.
    public Queue<IdempotencySaveResult> SaveResults { get; } = new();

    public int FindCalls { get; private set; }

    public int SaveCalls { get; private set; }

    public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken)
    {
        FindCalls++;
        var record = Stored.TryGetValue(key, out var stored)
            ? stored
            : Saved.Select(s => s.Record).LastOrDefault(r => r.Key == key);
        return Task.FromResult(record);
    }

    public Task<IdempotencySaveResult> SaveAsync(IdempotencyRecord record, Payment payment, CancellationToken cancellationToken)
    {
        SaveCalls++;
        var result = SaveResults.Count > 0 ? SaveResults.Dequeue() : IdempotencySaveResult.Saved;
        if (result == IdempotencySaveResult.Saved)
        {
            Saved.Add((record, payment));
        }

        return Task.FromResult(result);
    }
}
