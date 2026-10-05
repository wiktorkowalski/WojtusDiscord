namespace DiscordEventService.Dtos;

// GET api/stats/memes — the "Meme index" dashboard page (#395).
public sealed record MemeIndexDto(
    // MemeIndex:AutomaticIndexing — the live hook and the weekly sweep.
    bool AutomaticIndexing,
    // The configured meme channels that have a row in channels.
    IReadOnlyList<MemeChannelDto> Channels,
    MemeStatusCountsDto Status,
    // meme_index rows a writer refused (refused_by_model_id is set), whatever their status.
    long RefusalCount,
    // Sum of file_size_bytes over every meme_index row, whatever its status.
    long TotalFileSizeBytes,
    // Indexed memes whose message is not deleted: what search can return. ByYear and every
    // distribution count this set, so it can be lower than Status.Indexed.
    long SearchableCount,
    IReadOnlyList<MemeWriterDto> Writers,
    // The newest indexed_at_utc of any annotation; null when there is none.
    DateTime? LastAnnotationAtUtc,
    MemeNotIndexedDto NotIndexed,
    // Every year from the first to the last one with a meme, years with none included.
    IReadOnlyList<MemeYearCountDto> ByYear,
    MemeDistributionsDto Distributions);

public sealed record MemeChannelDto(ulong ChannelDiscordId, string Name);

public sealed record MemeStatusCountsDto(long Pending, long Indexed, long Failed, long Skipped, long Total);

// One writer = one (model, prompt version). CoveragePercent = its annotations / indexed
// memes * 100, rounded to one decimal; 0 when nothing is indexed.
public sealed record MemeWriterDto(
    string ModelId,
    string PromptVersion,
    long AnnotationCount,
    double CoveragePercent,
    DateTime LastAnnotationAtUtc);

// The same number as memeWaitingCount of GET api/stats/overview (one cached scan, up to a
// minute old). OldestPostedAtUtc is null when nothing waits.
public sealed record MemeNotIndexedDto(long Count, DateTime? OldestPostedAtUtc);

// Year posted, in guild-local time (Europe/Warsaw).
public sealed record MemeYearCountDto(int Year, long Count);

// Each meme counts once: with several annotations, the latest one is read.
public sealed record MemeDistributionsDto(
    // Every value. Names: template_meme, screenshot_post_or_chat, comic, photo_with_caption,
    // cutout_face_or_emote, edited_photo, video_frame, other. Null = written before schema v2.
    MemeDistributionDto ImageKind,
    // Every value. Names: pl, en, mixed, none (= no text in the image). Null = written before schema v2.
    MemeDistributionDto Language,
    // Every value. "other" = a platform that is not on the list. Null = no platform visible.
    MemeDistributionDto Source,
    // The top templates. No null bucket; MissingCount = memes with no template.
    MemeDistributionDto Templates,
    // The top franchises. No null bucket; MissingCount = memes with no franchise.
    MemeDistributionDto Franchises,
    // The top tags. No null bucket; MissingCount = memes with no tag.
    MemeDistributionDto Tags);

// Buckets: count descending, then name (ordinal). For ImageKind, Language and Source a bucket
// with Name null comes last when at least one meme has no value; it is never merged into
// "other" or "none". DistinctCount = the number of distinct non-null values, also those
// outside a top list. MissingCount = memes with no value (null column or empty list).
public sealed record MemeDistributionDto(
    IReadOnlyList<MemeBucketDto> Buckets,
    int DistinctCount,
    long MissingCount);

public sealed record MemeBucketDto(string? Name, long Count);

// GET api/stats/memes/search-usage — from meme_search_log, without the person and the channel.
// The dashboard's own tester searches are never counted.
public sealed record MemeSearchUsageDto(
    int Days,
    // Searches started in the window: slash command + assistant tool + other. A page turn
    // (BySource.PageButton) is not a new search and is in none of the numbers below.
    long SearchCount,
    long ZeroResultCount,
    // ZeroResultCount / SearchCount, 0..1; null when SearchCount is 0.
    double? ZeroResultRate,
    // 95th percentile of the search SQL duration; null when SearchCount is 0.
    double? DurationP95Ms,
    MemeSearchSourceCountsDto BySource,
    // The newest started searches, newest first.
    IReadOnlyList<MemeLoggedSearchDto> Latest);

public sealed record MemeSearchSourceCountsDto(long SlashCommand, long AssistantTool, long PageButton, long Other);

// Source: slashCommand, assistantTool or other. TopHit is null for a search with no hits.
public sealed record MemeLoggedSearchDto(
    DateTime SearchedAtUtc,
    string Query,
    string Source,
    int ResultCount,
    double DurationMs,
    MemeLoggedTopHitDto? TopHit);

// The first hit of a logged search. FileName, JumpUrl and ThumbnailUrl are null when the meme
// has left the index; DescriptionPl is null when the winning annotation is gone.
public sealed record MemeLoggedTopHitDto(
    ulong AttachmentDiscordId,
    double Score,
    string? FileName,
    string? DescriptionPl,
    string? JumpUrl,
    string? ThumbnailUrl);

// GET api/stats/memes/search — the "Try a search" tester. Total = matching memes, not only
// the returned ones. Score = TsRank + TrigramWeight * TrigramSimilarity for every hit.
public sealed record MemeSearchResultDto(
    string Query,
    int Total,
    double TrigramWeight,
    IReadOnlyList<MemeSearchHitDto> Hits);

// ImageKind: the names of MemeDistributionsDto.ImageKind; null = written before schema v2.
// ModelId and PromptVersion name the annotation that won for this meme. JumpUrl opens the
// Discord message. ThumbnailUrl is a path of this API that redirects to the image.
public sealed record MemeSearchHitDto(
    int Rank,
    ulong AttachmentDiscordId,
    ulong MessageDiscordId,
    ulong ChannelDiscordId,
    string FileName,
    string? DescriptionPl,
    string? DescriptionEn,
    string? ImageKind,
    IReadOnlyList<string> Templates,
    IReadOnlyList<string> Tags,
    DateTime PostedAtUtc,
    double Score,
    double TsRank,
    double TrigramSimilarity,
    string ModelId,
    string PromptVersion,
    string JumpUrl,
    string ThumbnailUrl);
