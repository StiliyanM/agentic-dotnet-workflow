namespace AgenticPayments.Application.Webhooks;

// The statuses that the provider can send. Mapped to PaymentStatus by the use case.
public enum ProviderPaymentStatus
{
    Succeeded,
    Failed,
}
