using AgenticPayments.Domain.Payments;
using AgenticPayments.Domain.Webhooks;

namespace AgenticPayments.Application.Webhooks;

public interface IWebhookEventRepository
{
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken);

    // Saves the event and the changed payment together. Returns false, and saves nothing,
    // when the event id is already stored (for example by a concurrent delivery).
    Task<bool> TryRecordAsync(ProcessedWebhookEvent webhookEvent, Payment payment, CancellationToken cancellationToken);
}
