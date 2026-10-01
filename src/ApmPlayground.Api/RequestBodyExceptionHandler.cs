using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.Options;

namespace ApmPlayground.Api;

// Maps request body binding failures (thrown because ThrowOnBadRequest is on) to a 400 problem.
public sealed class RequestBodyExceptionHandler(IOptions<JsonOptions> jsonOptions) : IExceptionHandler
{
    private const string PropertyPathPrefix = "$.";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        var errors = GetFieldErrors(httpContext, badRequest);
        IResult result = errors.Count > 0
            ? TypedResults.ValidationProblem(errors)
            : TypedResults.Problem(statusCode: badRequest.StatusCode);
        await result.ExecuteAsync(httpContext);
        return true;
    }

    private Dictionary<string, string[]> GetFieldErrors(HttpContext httpContext, BadHttpRequestException badRequest)
    {
        var errors = new Dictionary<string, string[]>();

        // An inner JsonException is the reader's syntax error: malformed JSON has no field errors.
        if (badRequest.InnerException is not JsonException json || json.InnerException is JsonException)
        {
            return errors;
        }

        var requestType = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint?.Metadata
            .GetMetadata<IAcceptsMetadata>()?.RequestType;
        if (requestType is null)
        {
            return errors;
        }

        var properties = jsonOptions.Value.SerializerOptions.GetTypeInfo(requestType).Properties;

        if (json.Path is { } path && path.StartsWith(PropertyPathPrefix, StringComparison.Ordinal))
        {
            var segment = path[PropertyPathPrefix.Length..].Split('.', '[')[0];
            var property = properties.FirstOrDefault(p => string.Equals(p.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (property is not null)
            {
                errors[property.Name] = [$"{ToFieldLabel(property.Name)} has an invalid value."];
            }

            return errors;
        }

        // A missing required member has no property path. This depends on the System.Text.Json message format:
        // the message lists the missing JSON names in single quotes. Only the contract's required names are searched.
        foreach (var property in properties.Where(p => p.IsRequired && json.Message.Contains($"'{p.Name}'", StringComparison.Ordinal)))
        {
            errors[property.Name] = [$"{ToFieldLabel(property.Name)} is required."];
        }

        return errors;
    }

    private static string ToFieldLabel(string name) => char.ToUpperInvariant(name[0]) + name[1..];
}
