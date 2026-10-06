using System.Diagnostics.CodeAnalysis;

namespace AgenticPayments.Application.Payments;

public sealed class CreatePaymentResult
{
    private CreatePaymentResult(
        CreatePaymentOutcome outcome,
        CreatePaymentResponse? response = null,
        IDictionary<string, string[]>? errors = null)
    {
        Outcome = outcome;
        Response = response;
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public CreatePaymentOutcome Outcome { get; }

    public CreatePaymentResponse? Response { get; }

    public IDictionary<string, string[]> Errors { get; }

    [MemberNotNullWhen(true, nameof(Response))]
    public bool IsSuccess => Response is not null;

    public static CreatePaymentResult Success(CreatePaymentResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new(CreatePaymentOutcome.Success, response);
    }

    public static CreatePaymentResult Invalid(IDictionary<string, string[]> errors) =>
        new(CreatePaymentOutcome.Invalid, errors: errors);

    public static CreatePaymentResult IdempotencyKeyReused() => new(CreatePaymentOutcome.IdempotencyKeyReused);

    public static CreatePaymentResult IdempotencyKeyInProgress() => new(CreatePaymentOutcome.IdempotencyKeyInProgress);
}
