namespace AgenticPayments.Application.Webhooks;

public enum ProviderWebhookOutcome
{
    Processed,
    Duplicate,
    PaymentNotFound,
    Invalid,
}
