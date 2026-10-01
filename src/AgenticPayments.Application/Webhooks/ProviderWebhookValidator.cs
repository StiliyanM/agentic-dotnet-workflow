using AgenticPayments.Domain.Webhooks;
using FluentValidation;

namespace AgenticPayments.Application.Webhooks;

public sealed class ProviderWebhookValidator : AbstractValidator<ProviderWebhookRequest>
{
    public ProviderWebhookValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(r => r.EventId)
            .NotEmpty().WithMessage("EventId is required.")
            .MaximumLength(ProcessedWebhookEvent.MaxEventIdLength)
            .WithMessage($"EventId must be at most {ProcessedWebhookEvent.MaxEventIdLength} characters.")
            .OverridePropertyName("eventId");

        RuleFor(r => r.PaymentId)
            .NotEmpty().WithMessage("PaymentId is required.")
            .OverridePropertyName("paymentId");

        RuleFor(r => r.Status)
            .IsInEnum().WithMessage("Status has an invalid value.")
            .OverridePropertyName("status");
    }
}
