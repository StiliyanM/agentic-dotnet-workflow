using ApmPlayground.Application.Payments;
using ApmPlayground.Domain.Payments;

namespace ApmPlayground.Infrastructure.Persistence;

public sealed class PaymentRepository(AppDbContext db) : IPaymentRepository
{
    public async Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
    }
}
