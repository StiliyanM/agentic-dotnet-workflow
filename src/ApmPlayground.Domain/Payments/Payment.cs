namespace ApmPlayground.Domain.Payments;

public class Payment
{
    // The amount column is numeric(AmountPrecision, AmountScale); MaxAmount is the largest value it can hold.
    public const int AmountPrecision = 18;
    public const int AmountScale = 2;
    public const decimal MaxAmount = 9999999999999999.99m;

    public Payment(decimal amount, Currency currency, PaymentMethod method)
    {
        Id = Guid.CreateVersion7();
        Amount = amount;
        Currency = currency;
        Method = method;
        Status = PaymentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    public PaymentMethod Method { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
