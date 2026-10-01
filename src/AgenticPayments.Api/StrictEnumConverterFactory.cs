using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenticPayments.Api;

// Applies StrictEnumConverter<TEnum> to every enum type.
public sealed class StrictEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        return typeToConvert.IsEnum;
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter?)Activator.CreateInstance(typeof(StrictEnumConverter<>).MakeGenericType(typeToConvert));
}
