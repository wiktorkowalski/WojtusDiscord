using System.Text.Json;

namespace DiscordEventService.Dtos;

/// <summary>One entry in the unified activity feed (a row of raw_event_logs).</summary>
public sealed record TimelineEventDto(
    Guid Id,
    string EventType,
    ulong GuildDiscordId,
    ulong? ChannelDiscordId,
    ulong? UserDiscordId,
    DateTime ReceivedAtUtc,
    int JsonSizeBytes,
    bool SerializationFailed,
    JsonElement Payload);

/// <summary>A keyset-paginated page of the timeline feed.</summary>
public sealed record TimelinePage(
    IReadOnlyList<TimelineEventDto> Events,
    string? NextCursor,
    bool HasMore);
