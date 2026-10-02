namespace AgenticPayments.Domain.Webhooks;

// payloadHash is null only for events stored before spec 003, which had no hash.
public sealed class ProcessedWebhookEvent(string eventId, Guid paymentId, string? payloadHash)
{
    public const int MaxEventIdLength = 200;
    public const int PayloadHashLength = 64;

    public string EventId { get; private set; } = eventId;

    public Guid PaymentId { get; private set; } = paymentId;

    public string? PayloadHash { get; private set; } = payloadHash;

    public DateTimeOffset ProcessedAt { get; private set; } = DateTimeOffset.UtcNow;
}
