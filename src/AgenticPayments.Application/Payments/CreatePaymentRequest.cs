using AgenticPayments.Domain.Payments;

namespace AgenticPayments.Application.Payments;

public sealed record CreatePaymentRequest
{
    public required decimal Amount { get; init; }

    public required Currency Currency { get; init; }

    public required PaymentMethod Method { get; init; }
}
