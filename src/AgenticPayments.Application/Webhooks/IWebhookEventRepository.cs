using AgenticPayments.Domain.Payments;
using AgenticPayments.Domain.Webhooks;

namespace AgenticPayments.Application.Webhooks;

public interface IWebhookEventRepository
{
    Task<ProcessedWebhookEvent?> FindAsync(string eventId, CancellationToken cancellationToken);

    // Saves the event and the current state of the payment in one transaction.
    // The payment must be tracked by the same scoped context, that is, loaded by IPaymentRepository.FindAsync
    // in this scope; otherwise InvalidOperationException.
    // DuplicateEvent: the event id is already stored. PaymentChanged: the payment changed after it was loaded.
    // In both cases nothing is saved and all tracked entities are forgotten, so the caller must load the payment again.
    Task<WebhookRecordResult> RecordAsync(ProcessedWebhookEvent webhookEvent, Payment payment, CancellationToken cancellationToken);
}
