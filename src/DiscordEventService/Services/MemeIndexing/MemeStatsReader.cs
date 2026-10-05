using System.Globalization;
using DiscordEventService.Configuration;
using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.MemeIndexing;

// Public because MemeStatsController is public; the reader itself stays internal.
public interface IMemeStatsReader
{
    Task<MemeIndexDto> GetIndexAsync(CancellationToken cancellationToken);

    Task<MemeSearchUsageDto> GetSearchUsageAsync(int days, CancellationToken cancellationToken);

    // Null = too many tester searches run right now (MemeDashboardLimits); nothing was searched.
    Task<MemeSearchResultDto?> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}

// The read side of the dashboard's "Meme index" page (#395). Read-only, with one exception that
// is not a write of this class: a tester search leaves its meme_search_log row, like every search.
internal sealed class MemeStatsReader(
    DiscordDbContext db,
    IMemeIndexSummaryReader summaryReader,
    MemeSearchService searchService,
    MemeDashboardLimits limits,
    IOptions<MemeIndexOptions> options) : IMemeStatsReader
{
    internal const int TopListSize = 8;
    internal const int LatestSearchCount = 20;

    private const string Tz = "Europe/Warsaw";
    private const int Indexed = (int)MemeIndexStatus.Indexed;

    // A search a person started. Not in the list: a page turn (a later page of a search that
    // is already counted) and the dashboard's tester. A new source is left out until it is added here.
    private static readonly MemeSearchSource[] StartedSearchSources =
        [MemeSearchSource.SlashCommand, MemeSearchSource.AssistantTool, MemeSearchSource.Other];

    // The tester's caller: no channel and no person.
    private static readonly MemeSearchCaller TesterCaller = new(MemeSearchSource.Dashboard, ChannelDiscordId: 0UL, UserDiscordId: 0UL);

    // What search can return, one annotation per meme. Search keeps the annotation with the best
    // score for the query and breaks a tie by indexed_at_utc DESC, model_id, prompt_version
    // (MemeSearchService). With no query there is no score, so the tiebreak alone picks: the
    // latest annotation. The same gate as search (Indexed, message not deleted), over every
    // guild; a second writer adds no count.
    private const string ChosenAnnotationsCte =
        """
        WITH chosen AS MATERIALIZED (
            SELECT DISTINCT ON (a.attachment_discord_id)
                   a.image_kind, a.language, a.source, a.franchise, a.templates, a.tags
            FROM meme_annotations AS a
            JOIN meme_index AS m ON m.id = a.meme_index_id
            JOIN messages AS msg ON msg.id = m.message_id
            WHERE m.status = {0}
              AND NOT msg.is_deleted
            ORDER BY a.attachment_discord_id, a.indexed_at_utc DESC, a.model_id, a.prompt_version
        )
        """;

    // SqlQueryRaw placeholders of the distribution queries: {0} (in the CTE) is the Indexed
    // status, {1} the size of a top list.
    private const string LimitParameter = "{1}";

    // The limit of a distribution that is not a top list.
    private const int EveryValue = int.MaxValue;

    // The page asks on every load and the endpoint has no auth: the answer is computed at
    // most once per MemeDashboardLimits.IndexCacheDuration, by one request at a time.
    public async Task<MemeIndexDto> GetIndexAsync(CancellationToken cancellationToken)
    {
        if (limits.FreshIndex() is { } cached)
            return cached;

        await limits.IndexGate.WaitAsync(cancellationToken);
        try
        {
            // Another request may have computed it while this one waited.
            if (limits.FreshIndex() is { } computedMeanwhile)
                return computedMeanwhile;

            var index = await ComputeIndexAsync(cancellationToken);
            limits.KeepIndex(index);
            return index;
        }
        finally
        {
            limits.IndexGate.Release();
        }
    }

    private async Task<MemeIndexDto> ComputeIndexAsync(CancellationToken cancellationToken)
    {
        var countsByStatus = await MemeIndexStatusQueries.CountByStatusAsync(db, cancellationToken);
        var status = new MemeStatusCountsDto(
            countsByStatus.GetValueOrDefault(MemeIndexStatus.Pending),
            countsByStatus.GetValueOrDefault(MemeIndexStatus.Indexed),
            countsByStatus.GetValueOrDefault(MemeIndexStatus.Failed),
            countsByStatus.GetValueOrDefault(MemeIndexStatus.Skipped),
            countsByStatus.Values.Sum());

        var writerCounts = await MemeIndexStatusQueries.CountByWriterAsync(db, cancellationToken);
        var writers = writerCounts
            .Select(w => new MemeWriterDto(
                w.ModelId,
                w.PromptVersion,
                w.Count,
                status.Indexed == 0 ? 0 : Math.Round(100.0 * w.Count / status.Indexed, 1),
                w.LastIndexedAtUtc))
            .ToList();

        var refusalCount = await db.MemeIndex.AsNoTracking()
            .LongCountAsync(m => m.RefusedByModelId != null, cancellationToken);
        var totalFileSizeBytes = await db.MemeIndex.AsNoTracking()
            .SumAsync(m => m.FileSizeBytes, cancellationToken);

        // The cached reader of the Overview page: both pages show one waiting number.
        var summary = await summaryReader.GetAsync(cancellationToken);

        var byYear = await GetByYearAsync(cancellationToken);

        return new MemeIndexDto(
            options.Value.AutomaticIndexing,
            await GetChannelsAsync(cancellationToken),
            status,
            refusalCount,
            totalFileSizeBytes,
            byYear.Sum(y => y.Count),
            writers,
            writerCounts.Count == 0 ? null : writerCounts.Max(w => w.LastIndexedAtUtc),
            new MemeNotIndexedDto(summary.Waiting, summary.OldestWaitingPostedAtUtc),
            byYear,
            await GetDistributionsAsync(cancellationToken));
    }

    public async Task<MemeSearchUsageDto> GetSearchUsageAsync(int days, CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        var started = StartedSearchSources.Select(s => (int)s).ToArray();
        var slashCommand = (int)MemeSearchSource.SlashCommand;
        var assistantTool = (int)MemeSearchSource.AssistantTool;
        var other = (int)MemeSearchSource.Other;
        var pageButton = (int)MemeSearchSource.PageButton;

        // Stays raw: percentile_cont has no EF LINQ translation. A page turn is counted on its
        // own and is not a search. Every other source, the dashboard's tester included, is not read.
        var totals = await db.Database.SqlQuery<SearchUsageRow>($"""
            SELECT count(*) FILTER (WHERE source = ANY({started}))::bigint AS search_count,
                   count(*) FILTER (WHERE source = ANY({started}) AND zero_results)::bigint AS zero_result_count,
                   (percentile_cont(0.95) WITHIN GROUP (ORDER BY duration_ms)
                       FILTER (WHERE source = ANY({started})))::float8 AS duration_p95ms,
                   count(*) FILTER (WHERE source = {slashCommand})::bigint AS slash_command_count,
                   count(*) FILTER (WHERE source = {assistantTool})::bigint AS assistant_tool_count,
                   count(*) FILTER (WHERE source = {pageButton})::bigint AS page_button_count,
                   count(*) FILTER (WHERE source = {other})::bigint AS other_count
            FROM meme_search_log
            WHERE searched_at_utc >= {since}
            """).SingleAsync(cancellationToken);

        return new MemeSearchUsageDto(
            days,
            totals.SearchCount,
            totals.ZeroResultCount,
            totals.SearchCount == 0 ? null : (double)totals.ZeroResultCount / totals.SearchCount,
            totals.DurationP95Ms,
            new MemeSearchSourceCountsDto(
                totals.SlashCommandCount, totals.AssistantToolCount, totals.PageButtonCount, totals.OtherCount),
            await GetLatestSearchesAsync(since, cancellationToken));
    }

    // The search scans every annotation and the endpoint has no auth: a fixed number may run
    // at one time, and one more is turned away at once instead of queued.
    public async Task<MemeSearchResultDto?> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        if (!await limits.SearchGate.WaitAsync(TimeSpan.Zero, cancellationToken))
            return null;

        try
        {
            return await SearchCoreAsync(query, limit, cancellationToken);
        }
        finally
        {
            limits.SearchGate.Release();
        }
    }

    private async Task<MemeSearchResultDto> SearchCoreAsync(string query, int limit, CancellationToken cancellationToken)
    {
        // The dashboard has no guild in its URL: the guild that holds the index (single-guild
        // deployment; with two, the one with more indexed memes, then the lower id).
        var guildId = await db.MemeIndex.AsNoTracking()
            .Where(m => m.Status == MemeIndexStatus.Indexed)
            .GroupBy(m => m.GuildDiscordId)
            .Select(g => new { GuildDiscordId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.GuildDiscordId)
            .Select(g => (ulong?)g.GuildDiscordId)
            .FirstOrDefaultAsync(cancellationToken);

        // Nothing is indexed: there is nothing to search, and no search to log.
        if (guildId is not { } guild)
            return new MemeSearchResultDto(query, 0, MemeSearchService.TrigramWeight, []);

        var page = await searchService.SearchPageAsync(guild, query, offset: 0, limit, TesterCaller, cancellationToken);

        var hits = page.Hits
            .Select((h, index) => new MemeSearchHitDto(
                index + 1,
                h.AttachmentDiscordId,
                h.MessageDiscordId,
                h.ChannelDiscordId,
                h.FileName,
                h.DescriptionPl,
                h.DescriptionEn,
                h.ImageKind is { } kind ? MemeJsonNames.Of(kind) : null,
                h.Templates,
                h.Tags,
                h.MessageCreatedAtUtc,
                h.Score,
                h.TsRank,
                h.TrigramSimilarity,
                h.ModelId,
                h.PromptVersion,
                MemePageView.JumpLink(guild, h.ChannelDiscordId, h.MessageDiscordId),
                ThumbnailUrl(h.AttachmentDiscordId)))
            .ToList();

        return new MemeSearchResultDto(query, page.Total, MemeSearchService.TrigramWeight, hits);
    }

    private static string ThumbnailUrl(ulong attachmentDiscordId) =>
        $"/{MemeStatsController.RoutePrefix}/{MemeStatsController.ThumbnailSegment}/{attachmentDiscordId}";

    private async Task<List<MemeChannelDto>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        var channelIds = options.Value.ChannelIds;
        if (channelIds.Length == 0)
            return [];

        return await db.Channels.AsNoTracking()
            .Where(c => channelIds.Contains(c.DiscordId))
            .OrderBy(c => c.DiscordId)
            .Select(c => new MemeChannelDto(c.DiscordId, c.Name))
            .ToListAsync(cancellationToken);
    }

    // Stays raw: the year is a guild-local calendar year (AT TIME ZONE), which the Npgsql EF
    // Core provider does not translate from LINQ. The same memes as the distributions.
    private async Task<List<MemeYearCountDto>> GetByYearAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<YearRow>($"""
            SELECT EXTRACT(YEAR FROM msg.created_at_utc AT TIME ZONE {Tz})::int AS year,
                   count(*)::bigint AS count
            FROM meme_index AS m
            JOIN messages AS msg ON msg.id = m.message_id
            WHERE m.status = {Indexed}
              AND NOT msg.is_deleted
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        // A year with no meme is a bar of height 0, not a missing bar.
        var countByYear = rows.ToDictionary(r => r.Year, r => r.Count);
        return Enumerable.Range(rows[0].Year, rows[^1].Year - rows[0].Year + 1)
            .Select(year => new MemeYearCountDto(year, countByYear.GetValueOrDefault(year)))
            .ToList();
    }

    // A closed set (image kind, language, source): every value, and the null bucket in the
    // list. A top list: the first TopListSize values, and the memes with no value in MissingCount.
    private async Task<MemeDistributionsDto> GetDistributionsAsync(CancellationToken cancellationToken) =>
        new MemeDistributionsDto(
            await GetDistributionAsync(
                ColumnDistributionSql("image_kind"), EveryValue, withNullBucket: true,
                value => MemeJsonNames.Of((MemeImageKind)ParseInt(value)), cancellationToken),
            await GetDistributionAsync(
                ColumnDistributionSql("language"), EveryValue, withNullBucket: true,
                value => MemeJsonNames.Of((MemeLanguage)ParseInt(value)), cancellationToken),
            await GetDistributionAsync(
                ColumnDistributionSql("source"), EveryValue, withNullBucket: true, value => value, cancellationToken),
            await GetDistributionAsync(
                ListDistributionSql("templates"), TopListSize, withNullBucket: false, value => value, cancellationToken),
            await GetDistributionAsync(
                ColumnDistributionSql("franchise"), TopListSize, withNullBucket: false, value => value, cancellationToken),
            await GetDistributionAsync(
                ListDistributionSql("tags"), TopListSize, withNullBucket: false, value => value, cancellationToken));

    // The order is made here, after the names are final: image_kind and language arrive as
    // numbers. SQL already cut a top list with the same rule (count, then the raw value).
    private async Task<MemeDistributionDto> GetDistributionAsync(
        string sql, int limit, bool withNullBucket, Func<string, string> nameOf, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<DistributionRow>(sql, Indexed, limit).ToListAsync(cancellationToken);

        // The row with no name counts the memes with no value.
        var named = rows.Where(r => r.Name is not null).ToList();
        var missingCount = rows.Sum(r => r.Count) - named.Sum(r => r.Count);

        var buckets = named
            .Select(r => new MemeBucketDto(nameOf(r.Name!), r.Count))
            .OrderByDescending(b => b.Count)
            .ThenBy(b => b.Name, StringComparer.Ordinal)
            .ToList();

        if (withNullBucket && missingCount > 0)
            buckets.Add(new MemeBucketDto(null, missingCount));

        return new MemeDistributionDto(buckets, named.Count == 0 ? 0 : (int)named[0].DistinctCount, missingCount);
    }

    // One value per meme. The column name is a literal of this class, never a value from a
    // request: it is SQL text, not a parameter. A value with no bucket (NULL) is always in the
    // result; the top list is cut among the others. COLLATE "C" makes the tiebreak the same on
    // every database.
    private static string ColumnDistributionSql(string column) =>
        $"""
        {ChosenAnnotationsCte}
        SELECT name, count, distinct_count
        FROM (
            SELECT {column}::text AS name,
                   count(*)::bigint AS count,
                   (count(*) FILTER (WHERE {column} IS NOT NULL) OVER ())::bigint AS distinct_count,
                   row_number() OVER (
                       PARTITION BY ({column} IS NULL)
                       ORDER BY count(*) DESC, {column}::text COLLATE "C") AS position
            FROM chosen
            GROUP BY {column}
        ) AS ranked
        WHERE name IS NULL OR position <= {LimitParameter}
        """;

    // A list column (templates, tags): a meme counts once for each of its distinct values.
    // The last SELECT is the row of the memes with an empty list (name NULL).
    private static string ListDistributionSql(string column) =>
        $"""
        {ChosenAnnotationsCte}
        SELECT name, count, distinct_count
        FROM (
            SELECT v.name,
                   count(*)::bigint AS count,
                   (count(*) OVER ())::bigint AS distinct_count,
                   row_number() OVER (ORDER BY count(*) DESC, v.name COLLATE "C") AS position
            FROM chosen
            CROSS JOIN LATERAL (SELECT DISTINCT unnest(chosen.{column}) AS name) AS v
            GROUP BY v.name
        ) AS ranked
        WHERE position <= {LimitParameter}
        UNION ALL
        SELECT NULL, count(*)::bigint, 0::bigint
        FROM chosen
        WHERE coalesce(cardinality({column}), 0) = 0
        HAVING count(*) > 0
        """;

    // The person and the channel of a search are not read here at all (#395).
    private async Task<List<MemeLoggedSearchDto>> GetLatestSearchesAsync(DateTime since, CancellationToken cancellationToken)
    {
        var searches = await db.MemeSearchLog.AsNoTracking()
            .Where(s => StartedSearchSources.Contains(s.Source) && s.SearchedAtUtc >= since)
            .OrderByDescending(s => s.SearchedAtUtc)
            .ThenBy(s => s.Id)
            .Take(LatestSearchCount)
            .Select(s => new
            {
                s.SearchedAtUtc,
                s.Query,
                s.Source,
                s.ResultCount,
                s.DurationMs,
                TopHit = s.Results
                    .OrderBy(r => r.Rank)
                    .Select(r => new { r.AttachmentDiscordId, r.ModelId, r.PromptVersion, r.Score })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var attachmentIds = searches
            .Where(s => s.TopHit is not null)
            .Select(s => s.TopHit!.AttachmentDiscordId)
            .Distinct()
            .ToList();

        var memes = await db.MemeIndex.AsNoTracking()
            .Where(m => attachmentIds.Contains(m.AttachmentDiscordId))
            .Select(m => new { m.AttachmentDiscordId, m.GuildDiscordId, m.ChannelDiscordId, m.MessageDiscordId, m.FileName })
            .ToDictionaryAsync(m => m.AttachmentDiscordId, cancellationToken);

        // The description of the annotation that won at search time, when it is still there.
        var descriptions = (await db.MemeAnnotations.AsNoTracking()
                .Where(a => attachmentIds.Contains(a.AttachmentDiscordId))
                .Select(a => new { a.AttachmentDiscordId, a.ModelId, a.PromptVersion, a.DescriptionPl })
                .ToListAsync(cancellationToken))
            .ToDictionary(a => (a.AttachmentDiscordId, a.ModelId, a.PromptVersion), a => a.DescriptionPl);

        return searches
            .Select(s =>
            {
                MemeLoggedTopHitDto? topHit = null;
                if (s.TopHit is { } hit)
                {
                    var meme = memes.GetValueOrDefault(hit.AttachmentDiscordId);
                    topHit = new MemeLoggedTopHitDto(
                        hit.AttachmentDiscordId,
                        hit.Score,
                        meme?.FileName,
                        descriptions.GetValueOrDefault((hit.AttachmentDiscordId, hit.ModelId, hit.PromptVersion)),
                        meme is null ? null : MemePageView.JumpLink(meme.GuildDiscordId, meme.ChannelDiscordId, meme.MessageDiscordId),
                        meme is null ? null : ThumbnailUrl(hit.AttachmentDiscordId));
                }

                return new MemeLoggedSearchDto(s.SearchedAtUtc, s.Query, SourceName(s.Source), s.ResultCount, s.DurationMs, topHit);
            })
            .ToList();
    }

    private static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);

    private static string SourceName(MemeSearchSource source) => source switch
    {
        MemeSearchSource.SlashCommand => "slashCommand",
        MemeSearchSource.AssistantTool => "assistantTool",
        _ => "other",
    };

    // SqlQuery binds result columns to these by snake_case name (DurationP95Ms is duration_p95ms).
    private sealed record YearRow(int Year, long Count);

    private sealed record DistributionRow(string? Name, long Count, long DistinctCount);

    private sealed record SearchUsageRow(
        long SearchCount,
        long ZeroResultCount,
        double? DurationP95Ms,
        long SlashCommandCount,
        long AssistantToolCount,
        long PageButtonCount,
        long OtherCount);
}
