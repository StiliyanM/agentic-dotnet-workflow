using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgenticPayments.Infrastructure.Persistence;

public sealed class IdempotencyRecordRepository(AppDbContext db) : IIdempotencyRecordRepository
{
    // Tracked on purpose: SaveAsync saves a Renew on the record that this lookup loaded.
    public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords.SingleOrDefaultAsync(r => r.Key == key, cancellationToken);

    public async Task<IdempotencySaveResult> SaveAsync(
        IdempotencyRecord record,
        Payment payment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(payment);

        // One SaveChanges (one transaction) stores the payment and the record together, or neither.
        // A tracked record is an update that checks the xmin token; a detached record is a new insert.
        db.Payments.Add(payment);
        if (db.Entry(record).State == EntityState.Detached)
        {
            db.IdempotencyRecords.Add(record);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return IdempotencySaveResult.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return IdempotencySaveResult.KeyConflict;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return IdempotencySaveResult.KeyConflict;
        }
    }
}
