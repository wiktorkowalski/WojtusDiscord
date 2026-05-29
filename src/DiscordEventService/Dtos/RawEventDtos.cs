using System.Text.Json;

namespace DiscordEventService.Dtos;

/// <summary>Lightweight raw-event row (no payload) for the explorer grid.</summary>
public sealed record RawEventSummaryDto(
    Guid Id,
    string EventType,
    ulong GuildDiscordId,
    ulong? ChannelDiscordId,
    ulong? UserDiscordId,
    DateTime ReceivedAtUtc,
    int JsonSizeBytes,
    bool SerializationFailed);

/// <summary>A single raw event with its full parsed payload.</summary>
public sealed record RawEventDetailDto(
    Guid Id,
    string EventType,
    ulong GuildDiscordId,
    ulong? ChannelDiscordId,
    ulong? UserDiscordId,
    DateTime ReceivedAtUtc,
    int JsonSizeBytes,
    bool SerializationFailed,
    Guid? CorrelationId,
    JsonElement Payload);

/// <summary>An event type with its total count, for the explorer's filter dropdown.</summary>
public sealed record RawEventTypeDto(string EventType, int Count);
