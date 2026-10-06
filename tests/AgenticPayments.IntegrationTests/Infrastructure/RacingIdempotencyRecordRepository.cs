using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;

namespace AgenticPayments.IntegrationTests.Infrastructure;

// Scoped decorator over the real repository: the first lookup of each request waits until both racing requests
// have looked up the key, and the coordinator decides which save runs first.
public sealed class RacingIdempotencyRecordRepository(IIdempotencyRecordRepository inner, IdempotencyRaceCoordinator coordinator)
    : IIdempotencyRecordRepository
{
    private bool _lookedUpOnce;

    public async Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken)
    {
        var record = await inner.FindAsync(key, cancellationToken);
        if (!_lookedUpOnce)
        {
            _lookedUpOnce = true;
            await coordinator.KeyLookedUpAsync(cancellationToken);
        }

        return record;
    }

    public Task<IdempotencySaveResult> SaveAsync(IdempotencyRecord record, Payment payment, CancellationToken cancellationToken) =>
        coordinator.SaveInOrderAsync(() => inner.SaveAsync(record, payment, cancellationToken), cancellationToken);
}
