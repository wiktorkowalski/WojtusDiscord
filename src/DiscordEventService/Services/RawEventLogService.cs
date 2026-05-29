using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace DiscordEventService.Services;

/// <summary>
/// Outcome of serializing an event. <see cref="Error"/> is non-null when serialization failed,
/// in which case <see cref="Json"/> is a diagnostic stub (not the real payload).
/// </summary>
public sealed record EventSerializationResult(string Json, Exception? Error)
{
    public bool Failed => Error is not null;
}

public class RawEventLogService(DiscordDbContext dbContext, ILogger<RawEventLogService> logger)
{
    /// <summary>
    /// Marker value placed in the <c>error</c> field of a serialization-failure stub.
    /// Kept as a constant so detectors don't hard-code the literal.
    /// </summary>
    public const string SerializationFailedMarker = "Serialization failed";

    // #107 switched to Newtonsoft to fix System.Text.Json's
    // KeyNotFoundException('deprecated') on DSharpPlus's [JsonProperty]-tagged
    // properties (DiscordVoiceRegion.IsDeprecated). But Newtonsoft, when it
    // finds [JsonConverter(SnowflakeArrayAsDictionaryJsonConverter)] on a
    // DSharpPlus property, calls that converter's WriteJson which re-enters
    // the same serializer → unbounded recursion → CLR StackOverflow
    // (uncatchable) → container crash whenever a message-shaped event arrives.
    //
    // Fix: keep Newtonsoft (needed for [JsonProperty] handling) but use a
    // contract resolver that strips that specific converter from the metadata,
    // letting Newtonsoft fall back to default Dictionary serialization (the
    // resulting JSON shape is a dict of snowflakes instead of an array — fine
    // for raw_event_logs which is replay/debug, not a Discord-wire payload).
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new BypassRecursiveConverterResolver(),
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        MaxDepth = 32,
    };

    /// <summary>
    /// Serializes an event args object to JSON. On failure returns a diagnostic stub and reports
    /// the exception via <see cref="EventSerializationResult.Error"/> so the caller can flag the
    /// row and record the failure — the failure is NOT swallowed here (no log; the caller owns it,
    /// where it has handler/correlation context).
    /// </summary>
    public EventSerializationResult SerializeEvent<T>(T eventArgs) where T : class
    {
        try
        {
            return new EventSerializationResult(JsonConvert.SerializeObject(eventArgs, JsonSettings), null);
        }
        catch (Exception ex)
        {
            // Keep a diagnostic stub (error type/message stay queryable), but surface the failure
            // so the row is flagged and a FailedEvent is recorded instead of masquerading as success.
            var stub = JsonConvert.SerializeObject(new
            {
                Error = SerializationFailedMarker,
                EventType = typeof(T).Name,
                Message = ex.Message
            }, JsonSettings);
            return new EventSerializationResult(stub, ex);
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
        ulong? userId = null,
        Guid? correlationId = null,
        bool serializationFailed = false)
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
                ReceivedAtUtc = DateTime.UtcNow,
                CorrelationId = correlationId,
                SerializationFailed = serializationFailed
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
    public async Task<EventSerializationResult> SerializeAndLogAsync<T>(
        T eventArgs,
        string eventType,
        ulong guildId = 0,
        ulong? channelId = null,
        ulong? userId = null,
        Guid? correlationId = null) where T : class
    {
        var result = SerializeEvent(eventArgs);
        await LogRawEventAsync(eventType, result.Json, guildId, channelId, userId, correlationId, result.Failed);
        return result;
    }

    private sealed class BypassRecursiveConverterResolver : CamelCasePropertyNamesContractResolver
    {
        // DSharpPlus's converter re-enters serialization with the same settings
        // (including itself as a [JsonConverter] attribute), causing unbounded
        // recursion. Strip it so the default object/dictionary serializer runs.
        private const string ProblemConverterName = "SnowflakeArrayAsDictionaryJsonConverter";

        private static readonly HashSet<string> SensitiveProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            "VoiceToken"
        };

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            if (property.Converter?.GetType().Name == ProblemConverterName)
                property.Converter = null;
            if (property.ItemConverter?.GetType().Name == ProblemConverterName)
                property.ItemConverter = null;
            if (SensitiveProperties.Contains(member.Name))
                property.ShouldSerialize = _ => false;
            return property;
        }

        protected override JsonContract CreateContract(Type objectType)
        {
            var contract = base.CreateContract(objectType);
            if (contract.Converter?.GetType().Name == ProblemConverterName)
                contract.Converter = null;
            return contract;
        }
    }
}
