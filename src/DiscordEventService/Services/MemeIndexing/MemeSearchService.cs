using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.MemeIndexing;

// Who asked, for the search log (#384). Set by the caller from the Discord event, never by a model.
public sealed record MemeSearchCaller(MemeSearchSource Source, ulong ChannelDiscordId, ulong UserDiscordId);

public sealed record MemeSearchHit(
    ulong ChannelDiscordId,
    ulong MessageDiscordId,
    ulong AttachmentDiscordId,
    string FileName,
    string? DescriptionPl,
    string? DescriptionEn,
    string[] Tags,
    DateTime MessageCreatedAtUtc,
    double Score,
    // The annotation that won for this attachment, and the two parts of its score (#395):
    // Score = TsRank + MemeSearchService.TrigramWeight * TrigramSimilarity.
    MemeImageKind? ImageKind,
    string[] Templates,
    string ModelId,
    string PromptVersion,
    double TsRank,
    double TrigramSimilarity);

// One page of a search (#391). Total is the number of matching memes on every page, not the
// number shown. A page past the end has no hits and Total 0: the count comes with the rows.
public sealed record MemeSearchPage(List<MemeSearchHit> Hits, int Total);

public sealed class MemeSearchService(DiscordDbContext db, MemeSearchLogWriter searchLog)
{
    public const int DefaultLimit = 5;

    // k in the binding rank blend: ts_rank with the A/B/C setweight scheme
    // lands roughly in 0.1–0.9, word_similarity in 0–1; 0.5 lets a strong
    // trigram match compete with an OCR hit without drowning out tag hits.
    // Tuned against the seeded rows in MemeSearchServiceTests.
    internal const double TrigramWeight = 0.5;

    // Pinned by MemeIndexSchemaTests: the threshold at which Polish
    // inflections (postgres ~ postgresie) still match without false positives.
    private const double TrigramThreshold = 0.4;

    // Hex characters of the stop-list hash kept as its marker: enough to tell two lists apart.
    private const int StopListVersionLength = 12;

    // Polish and English function words kept out of the ts_rank query (#380). Compared with the
    // tokens before unaccent, hence both "sie" and "się". Measured on #370; the same list is
    // STOP_WORDS in tools/meme-eval/retrieval_eval.py.
    private static readonly HashSet<string> RankStopWords =
    [
        "w", "we", "z", "ze", "na", "do", "od", "o", "u", "i", "a", "po", "za", "to", "ten", "ta", "te", "tym",
        "jak", "co", "sie", "się", "że", "czy", "dla",
        "the", "an", "of", "in", "on", "at", "is", "and", "or", "for", "with"
    ];

    // The stop-list marker in the search log (#384): a hash of the list, so an edit of the list
    // changes it with no version to bump by hand.
    internal static readonly string RankStopListVersion = Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', RankStopWords.Order(StringComparer.Ordinal)))))[..StopListVersionLength];

    // Every call leaves one row in meme_search_log (#384), also a search with no hits. The row
    // is written after this method returns (MemeSearchLogWriter), so it adds no wait and its
    // failure cannot reach the caller. A search that throws leaves no row.
    public async Task<List<MemeSearchHit>> SearchAsync(
        ulong guildId, string query, int limit, MemeSearchCaller caller, CancellationToken cancellationToken) =>
        (await SearchPageAsync(guildId, query, offset: 0, limit, caller, cancellationToken)).Hits;

    // The same search from a given offset, with the total (#391). Each page is its own row in the log.
    public async Task<MemeSearchPage> SearchPageAsync(
        ulong guildId, string query, int offset, int limit, MemeSearchCaller caller, CancellationToken cancellationToken)
    {
        var searchedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        // Two token lists (#380). The filter keeps every token: a function word is often what lets
        // an inflected query through ("steamie" is not "steam" in 'simple'), and the trigram
        // score then ranks the row. The rank query drops function words, which otherwise score
        // like content words against the weight-A search_phrases.
        var tokens = Tokenize(query);
        var contentTokens = tokens.Where(t => !RankStopWords.Contains(t)).ToList();
        var rankTokens = contentTokens.Count > 0 ? contentTokens : tokens;

        // A query with no word characters runs no SQL. It is still a search, and is logged as one.
        var rows = tokens.Count == 0
            ? []
            : await QueryAsync(guildId, query, tokens, rankTokens, offset, limit, cancellationToken);

        searchLog.Write(NewLogRow(searchedAtUtc, guildId, query, offset, limit, caller, tokens, rankTokens, rows, stopwatch.Elapsed));

        var hits = rows
            .Select(r => new MemeSearchHit(
                (ulong)r.ChannelDiscordId,
                (ulong)r.MessageDiscordId,
                (ulong)r.AttachmentDiscordId,
                r.FileName,
                r.DescriptionPl,
                r.DescriptionEn,
                r.Tags ?? [],
                r.MessageCreatedAtUtc,
                r.Score,
                (MemeImageKind?)r.ImageKind,
                r.Templates ?? [],
                r.ModelId,
                r.PromptVersion,
                r.TsRank,
                r.TrigramSimilarity))
            .ToList();

        return new MemeSearchPage(hits, rows.Count > 0 ? (int)rows[0].TotalCount : 0);
    }

    private Task<List<MemeSearchRow>> QueryAsync(
        ulong guildId, string query, List<string> tokens, List<string> rankTokens, int offset, int limit, CancellationToken cancellationToken)
    {
        // OR not AND: the 'simple' config keeps stopwords, so AND-semantics would
        // zero out natural-language queries. Tokens are alphanumeric-only, so the
        // OR operator cannot inject other tsquery syntax (&, !, parens, prefix stars).
        var filterQuery = string.Join(" | ", tokens);
        var rankQuery = string.Join(" | ", rankTokens);
        var guild = (long)guildId;
        var indexed = (int)MemeIndexStatus.Indexed;

        // Stays raw: the custom public.f_unaccent function and the setweight ts_rank
        // + word_similarity blend have no EF LINQ translation.
        // Column names are snake_case: the EFCore.NamingConventions plugin
        // applies to SqlQuery DTOs too, so MemeSearchRow.Score binds to
        // "score", MessageCreatedAtUtc to "message_created_at_utc", etc.
        //
        // An attachment has one annotation per model + prompt version (#367). DISTINCT ON keeps
        // its best-scoring one, so the score is max(score) and the description/tags shown come
        // from the row that earned it — a weak or wrong annotation can add a hit, never hide one.
        // LIMIT applies after that, to attachments.
        //
        // total_count is a window over the outer rows, before LIMIT: it counts attachments, the
        // same on every page (#391). attachment_discord_id ends the outer ORDER BY because paging
        // needs a total order: two attachments of one message can tie on score and time, and a
        // tie may come back in a different order on the next page.
        //
        // model_id, prompt_version, ts_rank and trigram_similarity are for the search log (#384)
        // and the dashboard tester (#395): the winning annotation and the two parts of its score.
        // image_kind and templates are for the tester only. Nothing in the ranking reads them.
        return db.Database.SqlQuery<MemeSearchRow>($"""
            SELECT channel_discord_id,
                   message_discord_id,
                   attachment_discord_id,
                   file_name,
                   description_pl,
                   description_en,
                   tags,
                   image_kind,
                   templates,
                   message_created_at_utc,
                   score,
                   model_id,
                   prompt_version,
                   ts_rank,
                   trigram_similarity,
                   count(*) OVER () AS total_count
            FROM (
                SELECT DISTINCT ON (m.attachment_discord_id)
                       m.channel_discord_id,
                       m.message_discord_id,
                       m.attachment_discord_id,
                       m.file_name,
                       a.description_pl,
                       a.description_en,
                       a.tags,
                       a.image_kind,
                       a.templates,
                       msg.created_at_utc AS message_created_at_utc,
                       (ts_rank(a.search_vector, to_tsquery('simple', public.f_unaccent({rankQuery})))
                        + {TrigramWeight} * word_similarity(public.f_unaccent({query}), a.search_text))::float8 AS score,
                       a.model_id,
                       a.prompt_version,
                       ts_rank(a.search_vector, to_tsquery('simple', public.f_unaccent({rankQuery})))::float8 AS ts_rank,
                       word_similarity(public.f_unaccent({query}), a.search_text)::float8 AS trigram_similarity
                FROM meme_annotations AS a
                JOIN meme_index AS m ON m.id = a.meme_index_id
                JOIN messages AS msg ON msg.id = m.message_id
                WHERE m.guild_discord_id = {guild}
                  AND m.status = {indexed}
                  AND NOT msg.is_deleted
                  AND (a.search_vector @@ to_tsquery('simple', public.f_unaccent({filterQuery}))
                       OR word_similarity(public.f_unaccent({query}), a.search_text) >= {TrigramThreshold})
                ORDER BY m.attachment_discord_id, score DESC, a.indexed_at_utc DESC, a.model_id, a.prompt_version
            ) AS best
            ORDER BY score DESC, message_created_at_utc DESC, attachment_discord_id
            LIMIT {limit} OFFSET {offset}
            """).ToListAsync(cancellationToken);
    }

    private static MemeSearchLogEntity NewLogRow(
        DateTime searchedAtUtc,
        ulong guildId,
        string query,
        int offset,
        int limit,
        MemeSearchCaller caller,
        List<string> tokens,
        List<string> rankTokens,
        List<MemeSearchRow> rows,
        TimeSpan duration) =>
        new MemeSearchLogEntity
        {
            SearchedAtUtc = searchedAtUtc,
            GuildDiscordId = guildId,
            ChannelDiscordId = caller.ChannelDiscordId,
            UserDiscordId = caller.UserDiscordId,
            Source = caller.Source,
            Query = query,
            Tokens = [.. tokens],
            RankTokens = [.. rankTokens],
            ResultOffset = offset,
            ResultLimit = limit,
            ResultCount = rows.Count,
            DurationMs = duration.TotalMilliseconds,
            TrigramWeight = TrigramWeight,
            TrigramThreshold = TrigramThreshold,
            StopListVersion = RankStopListVersion,
            Results = rows
                .Select((row, index) => new MemeSearchLogResultEntity
                {
                    Rank = offset + index + 1,
                    AttachmentDiscordId = (ulong)row.AttachmentDiscordId,
                    ModelId = row.ModelId,
                    PromptVersion = row.PromptVersion,
                    TsRank = row.TsRank,
                    TrigramSimilarity = row.TrigramSimilarity,
                    Score = row.Score
                })
                .ToList()
        };

    // Word characters only (letters incl. Polish, digits); everything else is
    // a separator. Lowercasing is cosmetic — tsquery and pg_trgm both fold
    // case — but keeps logs and tests deterministic.
    private static List<string> Tokenize(string query) =>
        query
            .ToLowerInvariant()
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0)
            .Distinct()
            .ToList();

    // SqlQuery maps result-set columns to properties by name; snowflakes come
    // back as bigint (the EF ulong columns are stored as int8), hence long here
    // with the unsigned cast applied in SearchAsync.
    private sealed record MemeSearchRow(
        long ChannelDiscordId,
        long MessageDiscordId,
        long AttachmentDiscordId,
        string FileName,
        string? DescriptionPl,
        string? DescriptionEn,
        string[]? Tags,
        int? ImageKind,
        string[]? Templates,
        DateTime MessageCreatedAtUtc,
        double Score,
        string ModelId,
        string PromptVersion,
        double TsRank,
        double TrigramSimilarity,
        long TotalCount);
}
