using ApmPlayground.Application.Payments;

namespace ApmPlayground.Api.Payments;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/payments", CreatePaymentAsync);
        return app;
    }

    private static async Task<IResult> CreatePaymentAsync(
        CreatePaymentRequest request,
        CreatePaymentUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Json(result.Response, statusCode: StatusCodes.Status201Created)
            : TypedResults.ValidationProblem(result.Errors);
    }
}
