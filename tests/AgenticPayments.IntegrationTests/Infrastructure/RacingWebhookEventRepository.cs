using AgenticPayments.Application.Webhooks;
using AgenticPayments.Domain.Payments;
using AgenticPayments.Domain.Webhooks;

namespace AgenticPayments.IntegrationTests.Infrastructure;

// Scoped decorator over the real repository: the coordinator decides which racing event saves first.
public sealed class RacingWebhookEventRepository(IWebhookEventRepository inner, WebhookRaceCoordinator coordinator)
    : IWebhookEventRepository
{
    public Task<ProcessedWebhookEvent?> FindAsync(string eventId, CancellationToken cancellationToken) =>
        inner.FindAsync(eventId, cancellationToken);

    public Task<WebhookRecordResult> RecordAsync(
        ProcessedWebhookEvent webhookEvent,
        Payment payment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);

        return coordinator.RecordInOrderAsync(
            webhookEvent.EventId,
            () => inner.RecordAsync(webhookEvent, payment, cancellationToken),
            cancellationToken);
    }
}
