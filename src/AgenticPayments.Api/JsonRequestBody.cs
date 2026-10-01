using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AgenticPayments.Api;

// Reads a JSON request body and maps each failure to the HTTP error contract:
// - the content type is not JSON: 415 with no body (the framework behavior before this reader);
// - the body is empty, is not valid JSON, or is not a JSON object: 400 problem with no field errors;
// - a required member is missing: "{Field} is required." under the field key;
// - a member value cannot be converted: "{Field} has an invalid value." under the field key.
// Missing members come from the contract metadata and the parsed document, not from exception messages.
public static class JsonRequestBody
{
    public static async Task<JsonRequestBodyResult<T>> ReadAsync<T>(
        HttpRequest request,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (!request.HasJsonContentType())
        {
            return new JsonRequestBodyResult<T>(TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType));
        }

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return new JsonRequestBodyResult<T>(TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest));
        }

        using (document)
        {
            return Read<T>(document.RootElement, options);
        }
    }

    private static JsonRequestBodyResult<T> Read<T>(JsonElement root, JsonSerializerOptions options)
        where T : class
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new JsonRequestBodyResult<T>(TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest));
        }

        var properties = options.GetTypeInfo(typeof(T)).Properties;
        var nameComparison = options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var errors = new Dictionary<string, string[]>();

        foreach (var property in properties.Where(p => p.IsRequired && !HasMember(root, p.Name, nameComparison)))
        {
            errors[property.Name] = [$"{ToFieldLabel(property.Name)} is required."];
        }

        try
        {
            var value = root.Deserialize<T>(options);
            if (errors.Count == 0 && value is not null)
            {
                return new JsonRequestBodyResult<T>(value);
            }
        }
        catch (JsonException exception)
        {
            // The serializer stops at the first value that it cannot convert. A missing required member
            // also throws here, but it is already in errors and has no member path.
            if (FindProperty(properties, exception.Path, nameComparison) is { } property)
            {
                errors.TryAdd(property.Name, [$"{ToFieldLabel(property.Name)} has an invalid value."]);
            }
        }

        return new JsonRequestBodyResult<T>(errors.Count > 0
            ? TypedResults.ValidationProblem(errors)
            : TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest));
    }

    private static bool HasMember(JsonElement root, string name, StringComparison comparison) =>
        root.EnumerateObject().Any(m => string.Equals(m.Name, name, comparison));

    private static JsonPropertyInfo? FindProperty(IList<JsonPropertyInfo> properties, string? path, StringComparison comparison)
    {
        const string memberPathPrefix = "$.";
        if (path is null || !path.StartsWith(memberPathPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var name = path[memberPathPrefix.Length..].Split('.', '[')[0];
        return properties.FirstOrDefault(p => string.Equals(p.Name, name, comparison));
    }

    private static string ToFieldLabel(string name) => char.ToUpperInvariant(name[0]) + name[1..];
}
