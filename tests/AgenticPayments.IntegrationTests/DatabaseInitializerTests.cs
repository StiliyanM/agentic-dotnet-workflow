using AgenticPayments.Application.Payments;
using AgenticPayments.Application.Webhooks;
using AgenticPayments.Domain.Payments;
using AgenticPayments.Domain.Webhooks;
using AgenticPayments.Infrastructure.Persistence;
using AgenticPayments.IntegrationTests.Infrastructure;
using AutoFixture;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticPayments.IntegrationTests;

// In the shared collection, so that no other test uses the table while the column is dropped.
[Collection(ApiCollection.Name)]
public sealed class DatabaseInitializerTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly Fixture _fixture = new();
    private readonly List<Guid> _paymentIds = [];
    private readonly List<string> _keys = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Restores the column if a test left it dropped, so that the other tests of the collection keep a valid schema.
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "ProcessedWebhookEvents" ADD COLUMN IF NOT EXISTS "PayloadHash" character varying(64) NULL""");
        // Restores the IdempotencyRecords table if a test left it dropped.
        await DatabaseInitializer.InitializeAsync(db, CancellationToken.None);
        if (_keys.Count > 0)
        {
            await db.IdempotencyRecords.Where(r => _keys.Contains(r.Key)).ExecuteDeleteAsync();
        }

        if (_paymentIds.Count > 0)
        {
            // The event rows are deleted with their payment (cascade).
            await db.Payments.Where(p => _paymentIds.Contains(p.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task InitializeAsync_PayloadHashColumnMissing_AddsNullableColumnAndKeepsRows()
    {
        var eventId = $"evt_{_fixture.Create<Guid>():N}";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payment = new Payment(_fixture.Create<int>() + 0.25m, _fixture.Create<Currency>(), _fixture.Create<PaymentMethod>());
        db.Payments.Add(payment);
        _paymentIds.Add(payment.Id);
        db.ProcessedWebhookEvents.Add(
            new ProcessedWebhookEvent(eventId, payment.Id, WebhookPayloadHash.Compute(payment.Id, ProviderPaymentStatus.Succeeded)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        // The schema of a database that spec 002 created.
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "ProcessedWebhookEvents" DROP COLUMN "PayloadHash" """);

        await DatabaseInitializer.InitializeAsync(db, CancellationToken.None);

        var column = await ReadPayloadHashColumnAsync(db);
        var storedEvent = await db.ProcessedWebhookEvents.AsNoTracking().SingleAsync(e => e.EventId == eventId);
        Assert.Multiple(
            () => Assert.Equal("YES:64", column),
            () => Assert.Equal(payment.Id, storedEvent.PaymentId),
            () => Assert.Null(storedEvent.PayloadHash));
    }

    [Fact]
    public async Task InitializeAsync_RunTwice_DoesNotFail()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await DatabaseInitializer.InitializeAsync(db, CancellationToken.None);
        await DatabaseInitializer.InitializeAsync(db, CancellationToken.None);

        var column = await ReadPayloadHashColumnAsync(db);
        var idempotencyTableExists = await IdempotencyTableExistsAsync(db);
        Assert.Multiple(
            () => Assert.Equal("YES:64", column),
            () => Assert.True(idempotencyTableExists));
    }

    [Fact]
    public async Task InitializeAsync_IdempotencyTableMissing_CreatesTable()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // The schema of a database that spec 003 created.
        await db.Database.ExecuteSqlRawAsync("""DROP TABLE "IdempotencyRecords" """);

        await DatabaseInitializer.InitializeAsync(db, CancellationToken.None);

        var payment = new Payment(_fixture.Create<int>() + 0.75m, _fixture.Create<Currency>(), _fixture.Create<PaymentMethod>());
        _paymentIds.Add(payment.Id);
        var key = _fixture.Create<Guid>().ToString();
        _keys.Add(key);
        var hash = CreatePaymentRequestHash.Compute(payment.Amount, payment.Currency, payment.Method);
        var createdAt = DateTimeOffset.UtcNow;
        db.Payments.Add(payment);
        db.IdempotencyRecords.Add(new IdempotencyRecord(key, hash, payment.Id, createdAt));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var tableExists = await IdempotencyTableExistsAsync(db);
        var stored = await db.IdempotencyRecords.AsNoTracking().SingleAsync(r => r.Key == key);
        Assert.Multiple(
            () => Assert.True(tableExists),
            () => Assert.Equal(payment.Id, stored.PaymentId),
            () => Assert.Equal(hash, stored.RequestHash),
            () => Assert.Equal(createdAt.ToUnixTimeMilliseconds(), stored.CreatedAt.ToUnixTimeMilliseconds()));
    }

    private static async Task<bool> IdempotencyTableExistsAsync(AppDbContext db)
    {
        var tables = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT table_name AS "Value"
                FROM information_schema.tables
                WHERE table_name = 'IdempotencyRecords'
                """)
            .ToListAsync();
        return tables.Count == 1;
    }

    // Returns "<is_nullable>:<character_maximum_length>" of the PayloadHash column; null when the column does not exist.
    private static async Task<string?> ReadPayloadHashColumnAsync(AppDbContext db)
    {
        var values = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT is_nullable || ':' || character_maximum_length::text AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'ProcessedWebhookEvents' AND column_name = 'PayloadHash'
                """)
            .ToListAsync();
        return values.SingleOrDefault();
    }
}
