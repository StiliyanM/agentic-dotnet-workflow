using Microsoft.EntityFrameworkCore;

namespace AgenticPayments.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    // EnsureCreated does not change an existing database, so a database from before spec 003 gets the
    // PayloadHash column here. Both statements are idempotent, so this runs safely on every start.
    public static async Task InitializeAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        await db.Database.EnsureCreatedAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "ProcessedWebhookEvents" ADD COLUMN IF NOT EXISTS "PayloadHash" character varying(64) NULL""",
            cancellationToken);
    }
}
