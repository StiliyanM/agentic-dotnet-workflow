using AgenticPayments.Application.Payments;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace AgenticPayments.Api.Payments;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/payments", CreatePaymentAsync);
        return app;
    }

    private static async Task<IResult> CreatePaymentAsync(
        HttpRequest httpRequest,
        IOptions<JsonOptions> jsonOptions,
        CreatePaymentUseCase useCase,
        CancellationToken cancellationToken)
    {
        var body = await JsonRequestBody.ReadAsync<CreatePaymentRequest>(
            httpRequest,
            jsonOptions.Value.SerializerOptions,
            cancellationToken);
        if (!body.IsSuccess)
        {
            return body.Error;
        }

        var result = await useCase.ExecuteAsync(body.Value, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Json(result.Response, statusCode: StatusCodes.Status201Created)
            : TypedResults.ValidationProblem(result.Errors);
    }
}
