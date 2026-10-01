using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;
using AgenticPayments.Domain.Webhooks;
using FluentValidation;

namespace AgenticPayments.Application.Webhooks;

public sealed class ProcessProviderWebhookUseCase(
    IValidator<ProviderWebhookRequest> validator,
    IPaymentRepository payments,
    IWebhookEventRepository webhookEvents)
{
    public async Task<ProviderWebhookResult> ExecuteAsync(ProviderWebhookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ProviderWebhookResult.Invalid(validationResult.ToDictionary());
        }

        // The duplicate check runs first, so a repeated event never touches the payment.
        if (await webhookEvents.ExistsAsync(request.EventId, cancellationToken))
        {
            return ProviderWebhookResult.Duplicate();
        }

        var payment = await payments.FindAsync(request.PaymentId, cancellationToken);
        if (payment is null)
        {
            return ProviderWebhookResult.PaymentNotFound();
        }

        payment.ChangeStatus(ToPaymentStatus(request.Status));
        var recorded = await webhookEvents.TryRecordAsync(
            new ProcessedWebhookEvent(request.EventId, request.PaymentId),
            payment,
            cancellationToken);

        return recorded ? ProviderWebhookResult.Processed() : ProviderWebhookResult.Duplicate();
    }

    private static PaymentStatus ToPaymentStatus(ProviderPaymentStatus status) => status switch
    {
        ProviderPaymentStatus.Succeeded => PaymentStatus.Succeeded,
        ProviderPaymentStatus.Failed => PaymentStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
