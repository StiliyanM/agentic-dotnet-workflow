using AgenticPayments.Application.Webhooks;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace AgenticPayments.Api.Webhooks;

public static partial class WebhookEndpoints
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
        ILoggerFactory loggerFactory,
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
        switch (result.Outcome)
        {
            case ProviderWebhookOutcome.Processed or ProviderWebhookOutcome.Ignored or ProviderWebhookOutcome.Duplicate:
                return TypedResults.Ok();
            case ProviderWebhookOutcome.DuplicatePayloadMismatch:
                // 200 keeps the provider from retrying; the warning makes the conflicting resend visible.
                LogPayloadMismatch(loggerFactory.CreateLogger(typeof(WebhookEndpoints)), body.Value.EventId);
                return TypedResults.Ok();
            case ProviderWebhookOutcome.PaymentNotFound:
                return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);
            case ProviderWebhookOutcome.Invalid:
                return TypedResults.ValidationProblem(result.Errors);
            default:
                throw new InvalidOperationException($"Unknown webhook outcome {result.Outcome}.");
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Webhook event {EventId} was received again with a different payload. It was not processed again.")]
    private static partial void LogPayloadMismatch(ILogger logger, string eventId);
}
