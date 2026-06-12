using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscordEventService.Infrastructure;

// Nullable counterpart to SnowflakeJsonConverter — same 2^53 JS-precision rationale.
internal sealed class NullableSnowflakeJsonConverter : JsonConverter<ulong?>
{
    public override ulong? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String when ulong.TryParse(reader.GetString(), out var v) => v,
            JsonTokenType.Number => reader.GetUInt64(),
            _ => throw new JsonException("Expected a snowflake as a string, number, or null."),
        };

    public override void Write(Utf8JsonWriter writer, ulong? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
        else writer.WriteNullValue();
    }
}
