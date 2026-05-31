using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class P5_2_LifecycleFactCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_bans_lifecycle",
                table: "bans",
                sql: "(is_active = false AND unbanned_at_utc IS NOT NULL) OR (is_active = true AND unbanned_at_utc IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_activities_lifecycle",
                table: "activities",
                sql: "(is_active = false AND ended_at_utc IS NOT NULL) OR (is_active = true AND ended_at_utc IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bans_lifecycle",
                table: "bans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_activities_lifecycle",
                table: "activities");
        }
    }
}
