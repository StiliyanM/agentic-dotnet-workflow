using AgenticPayments.Domain.Payments;

namespace AgenticPayments.Application.Payments;

public interface IPaymentRepository
{
    Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken);
}
