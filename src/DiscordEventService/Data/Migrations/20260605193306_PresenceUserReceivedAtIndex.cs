using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class PresenceUserReceivedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_presence_events_user_discord_id",
                table: "presence_events");

            migrationBuilder.CreateIndex(
                name: "ix_presence_events_user_discord_id_received_at_utc",
                table: "presence_events",
                columns: new[] { "user_discord_id", "received_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_presence_events_user_discord_id_received_at_utc",
                table: "presence_events");

            migrationBuilder.CreateIndex(
                name: "ix_presence_events_user_discord_id",
                table: "presence_events",
                column: "user_discord_id");
        }
    }
}
