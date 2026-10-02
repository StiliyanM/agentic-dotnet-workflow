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
    // The only status change is Pending -> terminal, and a terminal payment is never saved again,
    // so a second attempt after a concurrent change cannot conflict again.
    public const int MaxAttempts = 2;

    public async Task<ProviderWebhookResult> ExecuteAsync(ProviderWebhookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ProviderWebhookResult.Invalid(validationResult.ToDictionary());
        }

        var hash = WebhookPayloadHash.Compute(request.PaymentId, request.Status);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            // The duplicate check runs first, so a repeated event never touches the payment.
            var storedEvent = await webhookEvents.FindAsync(request.EventId, cancellationToken);
            if (storedEvent is not null)
            {
                // Events stored before spec 003 have no hash; they count as duplicates without a compare.
                return storedEvent.PayloadHash is null || storedEvent.PayloadHash == hash
                    ? ProviderWebhookResult.Duplicate()
                    : ProviderWebhookResult.DuplicatePayloadMismatch();
            }

            var payment = await payments.FindAsync(request.PaymentId, cancellationToken);
            if (payment is null)
            {
                return ProviderWebhookResult.PaymentNotFound();
            }

            var applied = payment.TryChangeStatus(ToPaymentStatus(request.Status));
            // RecordAsync saves the payment that payments.FindAsync loaded and tracks in this scope.
            var recordResult = await webhookEvents.RecordAsync(
                new ProcessedWebhookEvent(request.EventId, request.PaymentId, hash),
                payment,
                cancellationToken);
            switch (recordResult)
            {
                case WebhookRecordResult.Recorded:
                    return applied ? ProviderWebhookResult.Processed() : ProviderWebhookResult.Ignored();
                case WebhookRecordResult.DuplicateEvent:
                    return ProviderWebhookResult.Duplicate();
                case WebhookRecordResult.PaymentChanged:
                    // Another event changed the payment after it was loaded; evaluate this event again on fresh data.
                    continue;
                default:
                    throw new InvalidOperationException($"Unknown record result {recordResult}.");
            }
        }

        throw new InvalidOperationException(
            $"Webhook event {request.EventId} could not be recorded after {MaxAttempts} attempts because the payment kept changing.");
    }

    private static PaymentStatus ToPaymentStatus(ProviderPaymentStatus status) => status switch
    {
        ProviderPaymentStatus.Succeeded => PaymentStatus.Succeeded,
        ProviderPaymentStatus.Failed => PaymentStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
