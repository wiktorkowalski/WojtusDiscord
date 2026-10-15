using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationUsageCacheTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cache_read_tokens",
                table: "conversation_usage",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cache_write_tokens",
                table: "conversation_usage",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cache_read_tokens",
                table: "conversation_usage");

            migrationBuilder.DropColumn(
                name: "cache_write_tokens",
                table: "conversation_usage");
        }
    }
}
