using AgenticPayments.Application.Webhooks;
using AgenticPayments.Domain.Payments;
using AgenticPayments.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgenticPayments.Infrastructure.Persistence;

public sealed class WebhookEventRepository(AppDbContext db) : IWebhookEventRepository
{
    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken) =>
        db.ProcessedWebhookEvents.AnyAsync(e => e.EventId == eventId, cancellationToken);

    public async Task<bool> TryRecordAsync(
        ProcessedWebhookEvent webhookEvent,
        Payment payment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);
        ArgumentNullException.ThrowIfNull(payment);

        // The payment is tracked by this scoped context, so one SaveChanges (one transaction)
        // stores the event and the status change together, or neither.
        db.ProcessedWebhookEvents.Add(webhookEvent);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }
}
