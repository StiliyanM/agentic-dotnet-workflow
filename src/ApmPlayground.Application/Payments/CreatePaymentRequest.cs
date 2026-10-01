using ApmPlayground.Domain.Payments;

namespace ApmPlayground.Application.Payments;

public record CreatePaymentRequest
{
    public required decimal Amount { get; init; }

    public required Currency Currency { get; init; }

    public required PaymentMethod Method { get; init; }
}
