using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Services.MemeIndexing;

// A closed set: only the exact JSON name of a member is read. A number, an unknown name or a
// comma list fails deserialization (reported as a schema violation). JsonStringEnumConverter is
// not used on purpose: it ORs "a, b" into one value even without [Flags], so
// "cutout_face_or_emote, comic" became video_frame and skipped the cut-out rule.
internal sealed class ClosedEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> ByName =
        Enum.GetValues<TEnum>().ToDictionary(MemeJsonNames.Of, value => value, StringComparer.Ordinal);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && ByName.TryGetValue(reader.GetString()!, out var value))
            return value;

        throw new JsonException($"value is not in the closed set of {typeof(TEnum).Name}");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(MemeJsonNames.Of(value));
}

// The contract says "none", storage says NULL; anything outside MemeSources.Known is a schema violation.
internal sealed class MemeSourceJsonConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"source must be a string, got {reader.TokenType}");

        var value = reader.GetString()!;
        if (value == MemeSources.None)
            return null;
        if (!MemeSources.Known.Contains(value, StringComparer.Ordinal))
            throw new JsonException($"source '{value}' is not in the closed set");

        return value;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value ?? MemeSources.None);
}

internal static class MemeJsonNames
{
    public static string[] Of<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(Of)];

    public static string Of<TEnum>(TEnum value) where TEnum : struct, Enum =>
        typeof(TEnum).GetField(value.ToString())?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name
        ?? value.ToString();
}
