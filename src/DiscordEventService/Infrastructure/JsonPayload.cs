using System.Text.Json;

namespace DiscordEventService.Infrastructure;

/// <summary>Parses a stored jsonb payload to a <see cref="JsonElement"/>, falling back
/// to a wrapper object for diagnostic stubs / malformed text rather than throwing.</summary>
public static class JsonPayload
{
    public static JsonElement Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { raw = json });
        }
    }
}
