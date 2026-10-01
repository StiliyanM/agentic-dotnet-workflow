using ApmPlayground.Api.Data;

namespace ApmPlayground.Api.Payments;

public static class PaymentEndpoints
{
    private const string RedirectBaseUrl = "https://pay.example.com";

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/payments", CreatePaymentAsync);
        return app;
    }

    private static async Task<IResult> CreatePaymentAsync(CreatePaymentRequest request, AppDbContext db)
    {
        var errors = CreatePaymentValidator.Validate(request);
        if (errors.Count > 0 || !PaymentMethodNames.TryParse(request.Method, out var method))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var payment = new Payment(request.Amount!.Value, request.Currency!, method);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var redirectUrl = $"{RedirectBaseUrl}/{PaymentMethodNames.ToName(method)}/{payment.Id:D}";
        return TypedResults.Json(
            new CreatePaymentResponse(payment.Id, redirectUrl, payment.Status.ToString()),
            statusCode: StatusCodes.Status201Created);
    }
}
