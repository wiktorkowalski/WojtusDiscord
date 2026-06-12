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

// Builds the dashboard API's JsonSerializerOptions so tests can assert serialization through the exact same pipeline.
internal static class DashboardJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new SnowflakeJsonConverter());
        options.Converters.Add(new NullableSnowflakeJsonConverter());
    }

    public static JsonSerializerOptions CreateOptions()
    {
        // Mirror ASP.NET Core's defaults (camelCase) plus the snowflake converters.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }
}
