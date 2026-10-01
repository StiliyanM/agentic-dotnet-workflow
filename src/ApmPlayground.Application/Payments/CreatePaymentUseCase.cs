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

        // The validator already checks the method; TryParse is still needed to get the parsed enum value.
        if (!validationResult.IsValid || !PaymentMethodNames.TryParse(request.Method, out var method))
        {
            return CreatePaymentResult.Invalid(validationResult.ToDictionary());
        }

        var payment = new Payment(request.Amount!.Value, request.Currency!, method);
        await repository.AddAsync(payment, cancellationToken);

        var redirectUrl = $"{RedirectBaseUrl}/{PaymentMethodNames.ToName(method)}/{payment.Id:D}";
        return CreatePaymentResult.Success(new CreatePaymentResponse(payment.Id, redirectUrl, payment.Status.ToString()));
    }
}
