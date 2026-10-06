using AgenticPayments.Domain.Payments;

namespace AgenticPayments.Application.Payments;

public interface IIdempotencyRecordRepository
{
    // The record for this exact key (case-sensitive), or null. Expired records are returned too.
    // The record stays tracked by this scope, so that SaveAsync can save a Renew on it.
    Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken);

    // Stores the payment and the record together (one transaction): an insert for a new record, or an update for a
    // record that FindAsync of this scope returned. KeyConflict: another request stored or renewed the key first;
    // nothing is stored.
    Task<IdempotencySaveResult> SaveAsync(IdempotencyRecord record, Payment payment, CancellationToken cancellationToken);
}
