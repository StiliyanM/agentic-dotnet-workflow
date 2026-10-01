using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;

namespace AgenticPayments.UnitTests.Payments;

public sealed class FakePaymentRepository : IPaymentRepository
{
    public List<Payment> Added { get; } = [];

    public Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        Added.Add(payment);
        return Task.CompletedTask;
    }
}
