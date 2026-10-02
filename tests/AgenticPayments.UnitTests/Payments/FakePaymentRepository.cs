using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;

namespace AgenticPayments.UnitTests.Payments;

public sealed class FakePaymentRepository : IPaymentRepository
{
    // Holds the payments that AddAsync stored. Tests can also seed payments here for FindAsync.
    public List<Payment> Added { get; } = [];

    // A payment that FindAsync returns for an id instead of the one in Added.
    // Simulates a fresh load after another request changed the stored payment.
    public Dictionary<Guid, Payment> Reloaded { get; } = [];

    public int FindCalls { get; private set; }

    public Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        Added.Add(payment);
        return Task.CompletedTask;
    }

    public Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        FindCalls++;
        return Task.FromResult(Reloaded.TryGetValue(id, out var reloaded) ? reloaded : Added.SingleOrDefault(p => p.Id == id));
    }
}
