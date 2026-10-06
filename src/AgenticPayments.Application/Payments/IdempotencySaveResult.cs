namespace AgenticPayments.Application.Payments;

public enum IdempotencySaveResult
{
    Saved,
    KeyConflict,
}
