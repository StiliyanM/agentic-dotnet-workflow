namespace AgenticPayments.Domain.Payments;

public sealed class Payment(decimal amount, Currency currency, PaymentMethod method)
{
    // The amount column is numeric(AmountPrecision, AmountScale); MaxAmount is the largest value it can hold.
    public const int AmountPrecision = 18;
    public const int AmountScale = 2;
    public const decimal MaxAmount = 9999999999999999.99m;

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public decimal Amount { get; private set; } = amount;

    public Currency Currency { get; private set; } = currency;

    public PaymentMethod Method { get; private set; } = method;

    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void ChangeStatus(PaymentStatus status) => Status = status;
}
