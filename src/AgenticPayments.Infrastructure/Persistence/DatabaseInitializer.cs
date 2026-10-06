using Microsoft.EntityFrameworkCore;

namespace AgenticPayments.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    // EnsureCreated does not change an existing database, so an older database gets the PayloadHash column
    // (spec 003) and the IdempotencyRecords table (spec 007) here. All statements are idempotent, so this runs
    // safely on every start. They must match what EnsureCreated makes from the model.
    public static async Task InitializeAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        await db.Database.EnsureCreatedAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "ProcessedWebhookEvents" ADD COLUMN IF NOT EXISTS "PayloadHash" character varying(64) NULL""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "IdempotencyRecords" (
                "Key" character varying(100) NOT NULL,
                "RequestHash" character varying(64) NOT NULL,
                "PaymentId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_IdempotencyRecords" PRIMARY KEY ("Key"),
                CONSTRAINT "FK_IdempotencyRecords_Payments_PaymentId" FOREIGN KEY ("PaymentId")
                    REFERENCES "Payments" ("Id") ON DELETE CASCADE)
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_IdempotencyRecords_PaymentId" ON "IdempotencyRecords" ("PaymentId")""",
            cancellationToken);
    }
}
