namespace AgenticPayments.Application.Webhooks;

public enum WebhookRecordResult
{
    Recorded,
    DuplicateEvent,
    PaymentChanged,
}
