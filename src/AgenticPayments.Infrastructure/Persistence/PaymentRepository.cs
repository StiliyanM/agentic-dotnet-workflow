using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;

namespace AgenticPayments.Infrastructure.Persistence;

public sealed class PaymentRepository(AppDbContext db) : IPaymentRepository
{
    public async Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Payments.FindAsync([id], cancellationToken);
}
