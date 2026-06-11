using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.MemeIndexing;

// One ranked /meme hit. Snowflakes for the jump link + fresh-URL re-fetch,
// descriptions for the "why it matched" snippet.
public sealed record MemeSearchHit(
    ulong ChannelDiscordId,
    ulong MessageDiscordId,
    ulong AttachmentDiscordId,
    string FileName,
    string? DescriptionPl,
    string? DescriptionEn,
    DateTime MessageCreatedAtUtc,
    double Score);

// #224 ranked hybrid search over Indexed memes (binding design on #224, column
// semantics pinned by MemeIndexSchemaTests): an OR'd to_tsquery gate — NOT
// websearch AND-semantics, the 'simple' config keeps stopwords so AND would
// zero out natural-language queries — plus a word_similarity trigram rescue
// for Polish inflections and typos (no Polish stemmer exists). Relevance comes
// from the rank blend, not the gate; recency breaks ties.
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

        // Tokens are alphanumeric-only, so joining with the OR operator cannot
        // inject other tsquery syntax (&, !, parentheses, prefix stars).
        var orQuery = string.Join(" | ", tokens);
        var guild = (long)guildId;
        var indexed = (int)MemeIndexStatus.Indexed;

        // Column names are snake_case: the EFCore.NamingConventions plugin
        // applies to SqlQuery DTOs too, so MemeSearchRow.Score binds to
        // "score", MessageCreatedAtUtc to "message_created_at_utc", etc.
        var rows = await db.Database.SqlQuery<MemeSearchRow>($"""
            SELECT m.channel_discord_id,
                   m.message_discord_id,
                   m.attachment_discord_id,
                   m.file_name,
                   m.description_pl,
                   m.description_en,
                   msg.created_at_utc AS message_created_at_utc,
                   (ts_rank(m.search_vector, to_tsquery('simple', public.f_unaccent({orQuery})))
                    + {TrigramWeight} * word_similarity(public.f_unaccent({query}), m.search_text))::float8 AS score
            FROM meme_index AS m
            JOIN messages AS msg ON msg.id = m.message_id
            WHERE m.guild_discord_id = {guild}
              AND m.status = {indexed}
              AND NOT msg.is_deleted
              AND (m.search_vector @@ to_tsquery('simple', public.f_unaccent({orQuery}))
                   OR word_similarity(public.f_unaccent({query}), m.search_text) >= {TrigramThreshold})
            ORDER BY score DESC, msg.created_at_utc DESC
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
        DateTime MessageCreatedAtUtc,
        double Score);
}
