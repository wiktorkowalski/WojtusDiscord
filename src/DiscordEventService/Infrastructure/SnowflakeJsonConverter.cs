using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscordEventService.Infrastructure;

// Snowflakes exceed 2^53 and would lose precision if a browser parsed them as JS numbers, so they're emitted as quoted strings.
internal sealed class SnowflakeJsonConverter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String when ulong.TryParse(reader.GetString(), out var v) => v,
            JsonTokenType.Number => reader.GetUInt64(),
            _ => throw new JsonException("Expected a snowflake as a string or number."),
        };

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
