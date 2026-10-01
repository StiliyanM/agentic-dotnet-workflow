namespace AgenticPayments.Application.Webhooks;

public sealed record ProviderWebhookRequest
{
    public required string EventId { get; init; }

    public required Guid PaymentId { get; init; }

    public required ProviderPaymentStatus Status { get; init; }
}
