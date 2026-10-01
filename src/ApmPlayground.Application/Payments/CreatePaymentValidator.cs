using ApmPlayground.Domain.Payments;
using FluentValidation;

namespace ApmPlayground.Application.Payments;

public sealed class CreatePaymentValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(r => r.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than 0.")
            .LessThanOrEqualTo(Payment.MaxAmount).WithMessage("Amount is too large.")
            .Must(a => decimal.Round(a, Payment.AmountScale) == a).WithMessage("Amount must have at most 2 decimal places.")
            .OverridePropertyName("amount");

        RuleFor(r => r.Currency)
            .IsInEnum().WithMessage("Currency has an invalid value.")
            .OverridePropertyName("currency");

        RuleFor(r => r.Method)
            .IsInEnum().WithMessage("Method has an invalid value.")
            .OverridePropertyName("method");
    }
}
