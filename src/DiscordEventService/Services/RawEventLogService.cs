using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscordEventService.Services;

public class RawEventLogService(DiscordDbContext dbContext, ILogger<RawEventLogService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = 32
    };

    /// <summary>
    /// Serializes an event args object to JSON, handling any serialization errors gracefully.
    /// </summary>
    public string SerializeEvent<T>(T eventArgs) where T : class
    {
        try
        {
            return JsonSerializer.Serialize(eventArgs, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to serialize {EventType}, falling back to type info only", typeof(T).Name);
            return JsonSerializer.Serialize(new
            {
                Error = "Serialization failed",
                EventType = typeof(T).Name,
                Message = ex.Message
            }, JsonOptions);
        }
    }

    /// <summary>
    /// Logs a raw event to the RawEventLog table.
    /// </summary>
    public async Task LogRawEventAsync(
        string eventType,
        string eventJson,
        ulong guildId = 0,
        ulong? channelId = null,
        ulong? userId = null)
    {
        try
        {
            var rawEvent = new RawEventLogEntity
            {
                EventType = eventType,
                GuildDiscordId = guildId,
                ChannelDiscordId = channelId,
                UserDiscordId = userId,
                EventJson = eventJson,
                JsonSizeBytes = System.Text.Encoding.UTF8.GetByteCount(eventJson),
                ReceivedAtUtc = DateTime.UtcNow
            };

            await dbContext.RawEventLogs.AddAsync(rawEvent);
            // Note: SaveChanges is called by the event handler after all entities are added
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to log raw event {EventType}", eventType);
        }
    }

    /// <summary>
    /// Convenience method to serialize and log in one call.
    /// </summary>
    public async Task<string> SerializeAndLogAsync<T>(
        T eventArgs,
        string eventType,
        ulong guildId = 0,
        ulong? channelId = null,
        ulong? userId = null) where T : class
    {
        var json = SerializeEvent(eventArgs);
        await LogRawEventAsync(eventType, json, guildId, channelId, userId);
        return json;
    }
}
