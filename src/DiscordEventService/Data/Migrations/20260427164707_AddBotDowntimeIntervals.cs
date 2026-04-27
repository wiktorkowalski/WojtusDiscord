using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBotDowntimeIntervals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bot_downtime_intervals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    detection_method = table.Column<int>(type: "integer", nullable: false),
                    last_event_before_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_event_after_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_downtime_intervals", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bot_downtime_intervals_ended_at_utc",
                table: "bot_downtime_intervals",
                column: "ended_at_utc",
                filter: "ended_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bot_downtime_intervals_started_at_utc_ended_at_utc",
                table: "bot_downtime_intervals",
                columns: new[] { "started_at_utc", "ended_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_downtime_intervals");
        }
    }
}
