namespace DiscordEventService.Data.Entities.Events;

public class GuildMembersChunkEventEntity
{
    public Guid Id { get; set; }

    public ulong GuildDiscordId { get; set; }

    public int ChunkIndex { get; set; }
    public int ChunkCount { get; set; }
    public int MemberCount { get; set; }

    public string? MemberIdsJson { get; set; }

    public string? PresencesJson { get; set; }

    public string? Nonce { get; set; }

    public string? NotFoundIdsJson { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
