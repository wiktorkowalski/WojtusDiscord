using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationMessageAuthor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "author_id",
                table: "conversation_message",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "author_name",
                table: "conversation_message",
                type: "text",
                nullable: true);

            // Backfill the user rows stored before #461: the Discord message with the same text,
            // posted up to 2 minutes before the row, names the author. Only a row whose matches
            // all share one author is filled. DM rows find no message (DMs are not ingested) and
            // stay null; a DM has one human anyway. The name follows ShownName and
            // ConversationHistory.Sanitize, and est_tokens is recounted with the author line.
            migrationBuilder.Sql("""
                WITH matched AS (
                    SELECT cm.id AS row_id,
                           min(u.discord_id) AS author_id,
                           min(COALESCE(mb.nickname, u.global_name, u.username)) AS author_name
                    FROM conversation_message cm
                    JOIN conversation c ON c.id = cm.conversation_id
                    JOIN messages m ON m.content = cm.text
                        AND m.created_at_utc BETWEEN cm.created_at_utc - interval '2 minutes' AND cm.created_at_utc
                    JOIN users u ON u.id = m.author_id AND NOT u.is_bot
                    LEFT JOIN guilds g ON g.discord_id = c.guild_discord_id
                    LEFT JOIN members mb ON mb.user_id = u.id AND mb.guild_id = g.id
                    WHERE cm.role = 0 AND cm.author_id IS NULL
                    GROUP BY cm.id
                    HAVING count(DISTINCT u.discord_id) = 1
                ),
                named AS (
                    SELECT row_id, author_id,
                           COALESCE(NULLIF(left(btrim(regexp_replace(author_name, '[[:cntrl:]\[\]"]', ' ', 'g')), 64), ''), '?') AS author_name
                    FROM matched
                )
                UPDATE conversation_message cm
                SET author_id = named.author_id,
                    author_name = named.author_name,
                    est_tokens = greatest(1, length('[from: ' || named.author_name || ', id ' || named.author_id || ']' || E'\n' || cm.text) / 4)
                FROM named
                WHERE cm.id = named.row_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "author_id",
                table: "conversation_message");

            migrationBuilder.DropColumn(
                name: "author_name",
                table: "conversation_message");
        }
    }
}
