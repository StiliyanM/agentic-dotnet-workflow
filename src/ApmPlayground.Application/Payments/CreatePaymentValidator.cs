using ApmPlayground.Domain.Payments;
using FluentValidation;

namespace ApmPlayground.Application.Payments;

public sealed class CreatePaymentValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(r => r.Amount)
            .NotNull().WithMessage("Amount is required.")
            .GreaterThan(0).WithMessage("Amount must be greater than 0.")
            .LessThanOrEqualTo(Payment.MaxAmount).WithMessage("Amount is too large.")
            .Must(a => decimal.Round(a!.Value, Payment.AmountScale) == a.Value).WithMessage("Amount must have at most 2 decimal places.")
            .OverridePropertyName("amount");

        RuleFor(r => r.Currency)
            .Must(c => !string.IsNullOrEmpty(c)).WithMessage("Currency is required.")
            .Must(SupportedCurrencies.IsSupported).WithMessage(r => $"Currency '{r.Currency}' is not supported.")
            .OverridePropertyName("currency");

        RuleFor(r => r.Method)
            .Must(m => !string.IsNullOrEmpty(m)).WithMessage("Method is required.")
            .Must(m => PaymentMethodNames.TryParse(m, out _)).WithMessage("Method must be 'ideal' or 'klarna'.")
            .OverridePropertyName("method");
    }
}
