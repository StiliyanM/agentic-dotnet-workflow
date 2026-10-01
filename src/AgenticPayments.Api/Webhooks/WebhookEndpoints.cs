using AgenticPayments.Application.Webhooks;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace AgenticPayments.Api.Webhooks;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/provider", ProcessProviderWebhookAsync);
        return app;
    }

    private static async Task<IResult> ProcessProviderWebhookAsync(
        HttpRequest httpRequest,
        IOptions<ProviderWebhookOptions> webhookOptions,
        IOptions<JsonOptions> jsonOptions,
        ProcessProviderWebhookUseCase useCase,
        CancellationToken cancellationToken)
    {
        // The signature is checked before the body is parsed, so an unsigned request learns nothing about the contract.
        if (!await ProviderWebhookSignature.IsValidAsync(httpRequest, webhookOptions.Value.Secret, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var body = await JsonRequestBody.ReadAsync<ProviderWebhookRequest>(
            httpRequest,
            jsonOptions.Value.SerializerOptions,
            cancellationToken);
        if (!body.IsSuccess)
        {
            return body.Error;
        }

        var result = await useCase.ExecuteAsync(body.Value, cancellationToken);
        return result.Outcome switch
        {
            ProviderWebhookOutcome.Processed or ProviderWebhookOutcome.Duplicate => TypedResults.Ok(),
            ProviderWebhookOutcome.PaymentNotFound => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound),
            ProviderWebhookOutcome.Invalid => TypedResults.ValidationProblem(result.Errors),
            _ => throw new InvalidOperationException($"Unknown webhook outcome {result.Outcome}."),
        };
    }
}
