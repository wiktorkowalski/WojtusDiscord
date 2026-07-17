using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usage_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    cap = table.Column<int>(type: "integer", nullable: false),
                    window_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    spent_usd = table.Column<double>(type: "double precision", nullable: false),
                    limit_usd = table.Column<double>(type: "double precision", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_alerts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_usage_alerts_cap_window_start_utc_level_user_discord_id",
                table: "usage_alerts",
                columns: new[] { "cap", "window_start_utc", "level", "user_discord_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usage_alerts");
        }
    }
}
