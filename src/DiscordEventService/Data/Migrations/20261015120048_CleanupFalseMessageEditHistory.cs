using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class CleanupFalseMessageEditHistory : Migration
    {
        // A history row Discord sent for a re-signed attachment URL or a refreshed embed, not an
        // edit (#413). The same rule as MessageJson.IsRealChange (PR #427): content, flags, the set
        // of attachment Ids and the ordered (Url, Title, Description) of the embeds are all equal.
        private const string IsRefresh = """
            h.content_before IS NOT DISTINCT FROM h.content_after
            AND h.flags_before IS NOT DISTINCT FROM h.flags_after
            AND ARRAY(SELECT x->>'Id' FROM jsonb_array_elements(coalesce(h.attachments_before_json, '[]'::jsonb)) AS x ORDER BY 1)
                = ARRAY(SELECT x->>'Id' FROM jsonb_array_elements(coalesce(h.attachments_after_json, '[]'::jsonb)) AS x ORDER BY 1)
            AND (SELECT coalesce(jsonb_agg(jsonb_build_array(e.x->'Url', e.x->'Title', e.x->'Description') ORDER BY e.n), '[]'::jsonb)
                 FROM jsonb_array_elements(coalesce(h.embeds_before_json, '[]'::jsonb)) WITH ORDINALITY AS e(x, n))
                = (SELECT coalesce(jsonb_agg(jsonb_build_array(e.x->'Url', e.x->'Title', e.x->'Description') ORDER BY e.n), '[]'::jsonb)
                 FROM jsonb_array_elements(coalesce(h.embeds_after_json, '[]'::jsonb)) WITH ORDINALITY AS e(x, n))
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A refresh stamped its receive time as edited_at_utc. Put back Discord's own value from
            // the latest raw MessageUpdated: Discord omits edited_timestamp for a message never edited.
            // Runs before the DELETE, which removes the rows it matches on.
            migrationBuilder.Sql($"""
                UPDATE messages AS m
                SET edited_at_utc = (
                    SELECT (r.event_json->'message'->>'edited_timestamp')::timestamptz
                    FROM raw_event_logs AS r
                    WHERE r.event_type = 'MessageUpdated' AND r.event_json->'message'->>'id' = m.discord_id::text
                    ORDER BY r.received_at_utc DESC
                    LIMIT 1)
                WHERE EXISTS (
                    SELECT 1 FROM message_edit_history AS h
                    WHERE h.message_id = m.id AND h.edited_at_utc = m.edited_at_utc AND {IsRefresh});
                """);

            migrationBuilder.Sql($"DELETE FROM message_edit_history AS h WHERE {IsRefresh};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: the deleted rows and the old edited_at_utc are not restored.
        }
    }
}
