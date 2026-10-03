using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.MemeIndexing;

public sealed record MemeSearchHit(
    ulong ChannelDiscordId,
    ulong MessageDiscordId,
    ulong AttachmentDiscordId,
    string FileName,
    string? DescriptionPl,
    string? DescriptionEn,
    string[] Tags,
    DateTime MessageCreatedAtUtc,
    double Score);

public sealed class MemeSearchService(DiscordDbContext db)
{
    public const int DefaultLimit = 5;

    // k in the binding rank blend: ts_rank with the A/B/C setweight scheme
    // lands roughly in 0.1–0.9, word_similarity in 0–1; 0.5 lets a strong
    // trigram match compete with an OCR hit without drowning out tag hits.
    // Tuned against the seeded rows in MemeSearchServiceTests.
    private const double TrigramWeight = 0.5;

    // Pinned by MemeIndexSchemaTests: the threshold at which Polish
    // inflections (postgres ~ postgresie) still match without false positives.
    private const double TrigramThreshold = 0.4;

    public async Task<List<MemeSearchHit>> SearchAsync(
        ulong guildId, string query, int limit, CancellationToken cancellationToken)
    {
        var tokens = Tokenize(query);
        if (tokens.Count == 0)
            return [];

        // OR not AND: the 'simple' config keeps stopwords, so AND-semantics would
        // zero out natural-language queries. Tokens are alphanumeric-only, so the
        // OR operator cannot inject other tsquery syntax (&, !, parens, prefix stars).
        var orQuery = string.Join(" | ", tokens);
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
        var rows = await db.Database.SqlQuery<MemeSearchRow>($"""
            SELECT channel_discord_id,
                   message_discord_id,
                   attachment_discord_id,
                   file_name,
                   description_pl,
                   description_en,
                   tags,
                   message_created_at_utc,
                   score
            FROM (
                SELECT DISTINCT ON (m.attachment_discord_id)
                       m.channel_discord_id,
                       m.message_discord_id,
                       m.attachment_discord_id,
                       m.file_name,
                       a.description_pl,
                       a.description_en,
                       a.tags,
                       msg.created_at_utc AS message_created_at_utc,
                       (ts_rank(a.search_vector, to_tsquery('simple', public.f_unaccent({orQuery})))
                        + {TrigramWeight} * word_similarity(public.f_unaccent({query}), a.search_text))::float8 AS score
                FROM meme_annotations AS a
                JOIN meme_index AS m ON m.id = a.meme_index_id
                JOIN messages AS msg ON msg.id = m.message_id
                WHERE m.guild_discord_id = {guild}
                  AND m.status = {indexed}
                  AND NOT msg.is_deleted
                  AND (a.search_vector @@ to_tsquery('simple', public.f_unaccent({orQuery}))
                       OR word_similarity(public.f_unaccent({query}), a.search_text) >= {TrigramThreshold})
                ORDER BY m.attachment_discord_id, score DESC, a.indexed_at_utc DESC, a.model_id, a.prompt_version
            ) AS best
            ORDER BY score DESC, message_created_at_utc DESC
            LIMIT {limit}
            """).ToListAsync(cancellationToken);

        return rows
            .Select(r => new MemeSearchHit(
                (ulong)r.ChannelDiscordId,
                (ulong)r.MessageDiscordId,
                (ulong)r.AttachmentDiscordId,
                r.FileName,
                r.DescriptionPl,
                r.DescriptionEn,
                r.Tags ?? [],
                r.MessageCreatedAtUtc,
                r.Score))
            .ToList();
    }

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
        DateTime MessageCreatedAtUtc,
        double Score);
}
