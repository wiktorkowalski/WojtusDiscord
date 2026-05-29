using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscordEventService.Infrastructure;

/// <summary>
/// Serializes Discord snowflakes (<see cref="ulong"/>) as JSON strings. Snowflakes
/// exceed 2^53 and would lose precision if a browser parsed them as JS numbers, so
/// the entire dashboard API emits them as quoted strings. Registered globally in
/// <c>AddControllers().AddJsonOptions(...)</c>; also fires for boxed <c>ulong</c>
/// values inside the generic explorer's <c>Dictionary&lt;string, object?&gt;</c> rows.
/// </summary>
public sealed class SnowflakeJsonConverter : JsonConverter<ulong>
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

/// <summary>Nullable counterpart to <see cref="SnowflakeJsonConverter"/>.</summary>
public sealed class NullableSnowflakeJsonConverter : JsonConverter<ulong?>
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
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>
/// Builds the <see cref="JsonSerializerOptions"/> the dashboard API uses, so tests can
/// assert serialization (e.g. snowflake-as-string) through the exact same pipeline.
/// </summary>
public static class DashboardJson
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
