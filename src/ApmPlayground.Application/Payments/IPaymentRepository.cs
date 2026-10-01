using ApmPlayground.Domain.Payments;

namespace ApmPlayground.Application.Payments;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken);
}
