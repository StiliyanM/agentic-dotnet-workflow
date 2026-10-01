using ApmPlayground.Domain.Payments;
using FluentValidation;

namespace ApmPlayground.Application.Payments;

public sealed class CreatePaymentUseCase(IValidator<CreatePaymentRequest> validator, IPaymentRepository repository)
{
    private const string RedirectBaseUrl = "https://pay.example.com";

    public async Task<CreatePaymentResult> ExecuteAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return CreatePaymentResult.Invalid(validationResult.ToDictionary());
        }

        var payment = new Payment(request.Amount, request.Currency, request.Method);
        var redirectUrl = new Uri($"{RedirectBaseUrl}/{ToUrlSegment(payment.Method)}/{payment.Id:D}");
        await repository.AddAsync(payment, cancellationToken);

        return CreatePaymentResult.Success(new CreatePaymentResponse(payment.Id, redirectUrl, payment.Status));
    }

    private static string ToUrlSegment(PaymentMethod method) => method switch
    {
        PaymentMethod.Ideal => "ideal",
        PaymentMethod.Klarna => "klarna",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };
}
