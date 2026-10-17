using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConversationMessageRefused : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "refused",
                table: "conversation_message",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Turns from before #450 carry no refusal record. A turn with a user row and no
            // other row is one the API refused before any text: on prod (2026-10-17) all 21 such
            // turns are refusals with Loki "refused" lines. The rows stay; the window skips them.
            migrationBuilder.Sql("""
                UPDATE conversation_message cm
                SET refused = true
                WHERE cm.role = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM conversation_message other
                      WHERE other.conversation_id = cm.conversation_id
                        AND other.turn_index = cm.turn_index
                        AND other.role <> 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refused",
                table: "conversation_message");
        }
    }
}
