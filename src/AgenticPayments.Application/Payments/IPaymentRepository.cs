using AgenticPayments.Domain.Payments;

namespace AgenticPayments.Application.Payments;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken);
}
