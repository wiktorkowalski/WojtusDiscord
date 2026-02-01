using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public class GuildMembersChunkEventEntity
{
    public Guid Id { get; set; }

    public Guid? GuildId { get; set; }
    public ulong GuildDiscordId { get; set; }

    public int ChunkIndex { get; set; }
    public int ChunkCount { get; set; }
    public int MemberCount { get; set; }

    // JSON array of member IDs received in this chunk
    public string? MemberIdsJson { get; set; }

    // JSON array of presence data if requested
    public string? PresencesJson { get; set; }

    // Nonce used to identify the request
    public string? Nonce { get; set; }

    // JSON array of members that were not found (if querying specific users)
    public string? NotFoundIdsJson { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation property
    public GuildEntity? Guild { get; set; }
}
