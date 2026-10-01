using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;

namespace AgenticPayments.UnitTests.Payments;

public sealed class FakePaymentRepository : IPaymentRepository
{
    // Holds the payments that AddAsync stored. Tests can also seed payments here for FindAsync.
    public List<Payment> Added { get; } = [];

    public Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        Added.Add(payment);
        return Task.CompletedTask;
    }

    public Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Added.SingleOrDefault(p => p.Id == id));
}
