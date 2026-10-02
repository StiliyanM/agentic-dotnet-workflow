namespace AgenticPayments.Application.Webhooks;

public enum ProviderWebhookOutcome
{
    Processed,
    Ignored,
    Duplicate,
    DuplicatePayloadMismatch,
    PaymentNotFound,
    Invalid,
}
