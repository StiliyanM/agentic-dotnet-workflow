using AgenticPayments.Domain.Payments;
using FluentValidation;

namespace AgenticPayments.Application.Payments;

public sealed class IdempotencyKeyValidator : AbstractValidator<string>
{
    public IdempotencyKeyValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(k => k)
            .NotEmpty().WithMessage("Idempotency-Key header is required.")
            .MaximumLength(IdempotencyRecord.MaxKeyLength).WithMessage("Idempotency-Key header must be at most 100 characters.")
            .OverridePropertyName("Idempotency-Key");
    }
}
