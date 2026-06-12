namespace DiscordEventService.Data.Entities.Core;

public class BackfillCheckpointEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong GuildDiscordId { get; set; }
    public BackfillType Type { get; set; }
    public BackfillStatus Status { get; set; }

    public ulong? LastProcessedId { get; set; }
    public ulong? CurrentChannelId { get; set; }
    public int ProcessedCount { get; set; }
    public int? TotalCount { get; set; }

    public int ErrorCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorAtUtc { get; set; }

    public string? HangfireJobId { get; set; }

    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum BackfillType
{
    Roles = 0,
    Emojis = 1,
    Stickers = 2,
    Channels = 3,
    Members = 4,
    Messages = 5,
    Reactions = 6,

    // Meme indexing (#221) — not part of the weekly full-backfill chain;
    // triggered independently via /api/ops/meme-index.
    MemeIndex = 7
}

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum BackfillStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}
