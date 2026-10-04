namespace DiscordEventService.Data.Entities.Core;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum MemeSearchSource
{
    SlashCommand = 0,
    AssistantTool = 1,
    Other = 2
}

// One row per meme search (#384): what was typed, by whom, and the settings in force. The ranked
// hits are in meme_search_log_results. Snowflakes only, no FKs: a log row must not depend on
// the guild, channel or user rows being there. Per-person data: the assistant's query role
// has no SELECT here (AddMemeSearchLog) and SchemaCatalog hides both tables.
public class MemeSearchLogEntity
{
    public Guid Id { get; set; }

    public DateTime SearchedAtUtc { get; set; }

    public ulong GuildDiscordId { get; set; }

    // For the assistant tool: the channel of the conversation (a DM channel in a DM).
    public ulong ChannelDiscordId { get; set; }
    public ulong UserDiscordId { get; set; }

    public MemeSearchSource Source { get; set; }

    public string Query { get; set; } = "";

    // After Tokenize: what the filter query got. Empty = the query had no word characters,
    // and no SQL ran.
    public string[] Tokens { get; set; } = [];

    // What the ts_rank query got: Tokens without the stop list, or all of them when
    // nothing else is left (#380).
    public string[] RankTokens { get; set; } = [];

    public int ResultLimit { get; set; }
    public int ResultCount { get; set; }

    // Stored generated column (result_count = 0) — never set from code.
    public bool ZeroResults { get; set; }

    // The search SQL only, without the Discord answer.
    public double DurationMs { get; set; }

    // The settings in force. score = ts_rank + trigram_weight * trigram_similarity.
    public double TrigramWeight { get; set; }
    public double TrigramThreshold { get; set; }

    // MemeSearchService.RankStopListVersion: a hash of the stop list, so it changes with the list.
    public string StopListVersion { get; set; } = "";

    public List<MemeSearchLogResultEntity> Results { get; set; } = [];
}

// One returned hit of a logged search, in rank order (1 = first).
public class MemeSearchLogResultEntity
{
    public Guid Id { get; set; }

    public Guid SearchId { get; set; }

    public int Rank { get; set; }

    public ulong AttachmentDiscordId { get; set; }

    // The annotation that won for this attachment (#367): the key of meme_annotations
    // together with AttachmentDiscordId. Not an FK: an import can replace that row in place.
    public string ModelId { get; set; } = "";
    public string PromptVersion { get; set; } = "";

    public double TsRank { get; set; }
    public double TrigramSimilarity { get; set; }
    public double Score { get; set; }

    public MemeSearchLogEntity Search { get; set; } = null!;
}
