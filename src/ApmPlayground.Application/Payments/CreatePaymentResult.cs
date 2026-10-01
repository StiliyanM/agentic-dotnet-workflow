using System.Diagnostics.CodeAnalysis;

namespace ApmPlayground.Application.Payments;

public sealed class CreatePaymentResult
{
    private CreatePaymentResult(CreatePaymentResponse? response, IDictionary<string, string[]> errors)
    {
        Response = response;
        Errors = errors;
    }

    public CreatePaymentResponse? Response { get; }

    public IDictionary<string, string[]> Errors { get; }

    [MemberNotNullWhen(true, nameof(Response))]
    public bool IsSuccess => Response is not null;

    public static CreatePaymentResult Success(CreatePaymentResponse response) =>
        new(response, new Dictionary<string, string[]>());

    public static CreatePaymentResult Invalid(IDictionary<string, string[]> errors) => new(null, errors);
}
