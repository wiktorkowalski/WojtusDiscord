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

    public static readonly TimeSpan StaleInProgressAfter = TimeSpan.FromHours(1);

    // LastUpdatedUtc is bumped on every SaveChanges (ITimestamped) and a live backfill saves at
    // least once per batch, so it is a heartbeat: an InProgress row with no write for longer than
    // StaleInProgressAfter belongs to a process that died before writing a terminal status (#282).
    // Callers leave Status untouched — the next run's executor treats the surviving InProgress row
    // as an interrupted run and resumes from its cursor.
    public bool IsActivelyInProgress(DateTime nowUtc)
        => Status == BackfillStatus.InProgress && nowUtc - LastUpdatedUtc < StaleInProgressAfter;

    // Chain-guard predicate (#289): the orchestrator marks every checkpoint of a freshly enqueued
    // chain Pending (with its Hangfire job id) before the first job runs, so a fresh Pending row
    // means "chain queued but not started yet" and must block a second chain the same way a live
    // InProgress row does. Same staleness cutoff — a dead process can strand Pending rows too.
    public bool IsChainActive(DateTime nowUtc)
        => (Status == BackfillStatus.InProgress || Status == BackfillStatus.Pending)
            && nowUtc - LastUpdatedUtc < StaleInProgressAfter;
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
