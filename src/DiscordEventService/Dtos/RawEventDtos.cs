using System.Text.Json;

namespace DiscordEventService.Dtos;

public sealed record RawEventSummaryDto(
    Guid Id,
    string EventType,
    ulong GuildDiscordId,
    ulong? ChannelDiscordId,
    ulong? UserDiscordId,
    DateTime ReceivedAtUtc,
    int JsonSizeBytes,
    bool SerializationFailed);

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

public sealed record RawEventTypeDto(string EventType, int Count);
