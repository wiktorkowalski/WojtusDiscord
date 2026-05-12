using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHeartbeatLastHeartbeatUtcIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_bot_heartbeats_last_heartbeat_utc",
                table: "bot_heartbeats",
                column: "last_heartbeat_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bot_heartbeats_last_heartbeat_utc",
                table: "bot_heartbeats");
        }
    }
}
