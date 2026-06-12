using System.Text.Json;

namespace DiscordEventService.Infrastructure;

// Falls back to a wrapper object for diagnostic stubs / malformed jsonb rather than throwing.
internal static class JsonPayload
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
