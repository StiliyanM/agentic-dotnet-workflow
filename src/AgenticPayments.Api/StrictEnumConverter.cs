using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenticPayments.Api;

// Reads only a JSON string that equals one defined member name, ignoring case.
// Numbers, numeric strings and comma-separated names are rejected. Writes the member name.
// JsonStringEnumConverter cannot replace this: it accepts comma-separated names for enums without [Flags],
// so "ideal, klarna" would bind to Klarna (0 | 1).
public sealed class StrictEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            var name = Enum.GetNames<TEnum>().FirstOrDefault(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase));
            if (name is not null)
            {
                return Enum.Parse<TEnum>(name);
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToString());
    }
}
