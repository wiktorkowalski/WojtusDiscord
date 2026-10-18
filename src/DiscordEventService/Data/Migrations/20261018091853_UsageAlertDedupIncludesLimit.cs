using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class UsageAlertDedupIncludesLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usage_alerts_cap_window_start_utc_level_user_discord_id",
                table: "usage_alerts");

            migrationBuilder.CreateIndex(
                name: "ix_usage_alerts_dedup",
                table: "usage_alerts",
                columns: new[] { "cap", "window_start_utc", "level", "user_discord_id", "limit_usd" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usage_alerts_dedup",
                table: "usage_alerts");

            migrationBuilder.CreateIndex(
                name: "ix_usage_alerts_cap_window_start_utc_level_user_discord_id",
                table: "usage_alerts",
                columns: new[] { "cap", "window_start_utc", "level", "user_discord_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
