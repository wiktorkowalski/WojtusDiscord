using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class P3_5_ThreadStarterMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "starter_message_discord_id",
                table: "thread_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE thread_events te
                SET starter_message_discord_id = te.thread_discord_id
                WHERE te.event_type = 0
                  AND te.starter_message_discord_id IS NULL
                  AND EXISTS (
                    SELECT 1 FROM messages m
                    WHERE m.discord_id = te.thread_discord_id
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "starter_message_discord_id",
                table: "thread_events");
        }
    }
}
