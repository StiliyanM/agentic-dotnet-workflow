using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;

namespace AgenticPayments.IntegrationTests.Infrastructure;

// Scoped decorator over the real repository: the first load of each request waits until both racing requests have loaded.
public sealed class RacingPaymentRepository(IPaymentRepository inner, WebhookRaceCoordinator coordinator) : IPaymentRepository
{
    private bool _loadedOnce;

    public async Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var payment = await inner.FindAsync(id, cancellationToken);
        if (!_loadedOnce)
        {
            _loadedOnce = true;
            await coordinator.PaymentLoadedAsync(cancellationToken);
        }

        return payment;
    }
}
