namespace AgenticPayments.Application.Payments;

public enum CreatePaymentOutcome
{
    Success,
    Invalid,
    IdempotencyKeyReused,
    IdempotencyKeyInProgress,
}
