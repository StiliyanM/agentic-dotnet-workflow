namespace AgenticPayments.Domain.Webhooks;

public sealed class ProcessedWebhookEvent(string eventId, Guid paymentId)
{
    public const int MaxEventIdLength = 200;

    public string EventId { get; private set; } = eventId;

    public Guid PaymentId { get; private set; } = paymentId;

    public DateTimeOffset ProcessedAt { get; private set; } = DateTimeOffset.UtcNow;
}
