using AgenticPayments.Application.Webhooks;
using AgenticPayments.Domain.Payments;
using AgenticPayments.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgenticPayments.Infrastructure.Persistence;

public sealed class WebhookEventRepository(AppDbContext db) : IWebhookEventRepository
{
    public Task<ProcessedWebhookEvent?> FindAsync(string eventId, CancellationToken cancellationToken) =>
        db.ProcessedWebhookEvents.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == eventId, cancellationToken);

    public async Task<WebhookRecordResult> RecordAsync(
        ProcessedWebhookEvent webhookEvent,
        Payment payment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);
        ArgumentNullException.ThrowIfNull(payment);

        // The payment is saved only because this scoped context tracks it; a detached payment would be lost silently.
        if (db.Entry(payment).State == EntityState.Detached)
        {
            throw new InvalidOperationException("The payment must be loaded by the payment repository of the same scope.");
        }

        // One SaveChanges (one transaction) stores the event and the payment together, or neither.
        // The payment update checks the xmin token, so a payment that changed after it was loaded is not overwritten.
        db.ProcessedWebhookEvents.Add(webhookEvent);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return WebhookRecordResult.Recorded;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return WebhookRecordResult.PaymentChanged;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return WebhookRecordResult.DuplicateEvent;
        }
    }
}
