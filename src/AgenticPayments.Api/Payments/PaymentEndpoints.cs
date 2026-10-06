using AgenticPayments.Application.Payments;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace AgenticPayments.Api.Payments;

public static class PaymentEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

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

        // A missing header gives "", so the use case reports it with the field errors.
        var idempotencyKey = httpRequest.Headers[IdempotencyKeyHeader].ToString();
        var result = await useCase.ExecuteAsync(idempotencyKey, body.Value, cancellationToken);
        return result.Outcome switch
        {
            CreatePaymentOutcome.Success => TypedResults.Json(result.Response, statusCode: StatusCodes.Status201Created),
            CreatePaymentOutcome.Invalid => TypedResults.ValidationProblem(result.Errors),
            CreatePaymentOutcome.IdempotencyKeyReused => TypedResults.Problem(
                detail: "The Idempotency-Key was already used with a different request.",
                statusCode: StatusCodes.Status422UnprocessableEntity),
            CreatePaymentOutcome.IdempotencyKeyInProgress => TypedResults.Problem(
                detail: "Another request with the same Idempotency-Key was processed at the same time. Retry the request.",
                statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException($"Unknown create payment outcome {result.Outcome}."),
        };
    }
}
