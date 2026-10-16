using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistConversationWebSearches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_conversation_message_role_shape",
                table: "conversation_message");

            migrationBuilder.AddColumn<string>(
                name: "web_searches_json",
                table: "conversation_message",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversation_message_role_shape",
                table: "conversation_message",
                sql: "(role = 0 AND tool_calls_json IS NULL AND web_searches_json IS NULL AND tool_call_id IS NULL AND tool_result IS NULL) OR (role = 1 AND tool_call_id IS NULL AND tool_result IS NULL) OR (role = 2 AND tool_call_id IS NOT NULL AND tool_result IS NOT NULL AND tool_calls_json IS NULL AND web_searches_json IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_conversation_message_role_shape",
                table: "conversation_message");

            migrationBuilder.DropColumn(
                name: "web_searches_json",
                table: "conversation_message");

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversation_message_role_shape",
                table: "conversation_message",
                sql: "(role = 0 AND tool_calls_json IS NULL AND tool_call_id IS NULL AND tool_result IS NULL) OR (role = 1 AND tool_call_id IS NULL AND tool_result IS NULL) OR (role = 2 AND tool_call_id IS NOT NULL AND tool_result IS NOT NULL AND tool_calls_json IS NULL)");
        }
    }
}
