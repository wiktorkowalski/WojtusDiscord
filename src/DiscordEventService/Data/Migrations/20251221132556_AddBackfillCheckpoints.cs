using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBackfillCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "backfill_checkpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    last_processed_id = table.Column<long>(type: "bigint", nullable: true),
                    current_channel_id = table.Column<long>(type: "bigint", nullable: true),
                    processed_count = table.Column<int>(type: "integer", nullable: false),
                    total_count = table.Column<int>(type: "integer", nullable: true),
                    error_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    last_error_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    hangfire_job_id = table.Column<string>(type: "text", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backfill_checkpoints", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_backfill_checkpoints_guild_discord_id_type",
                table: "backfill_checkpoints",
                columns: new[] { "guild_discord_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_backfill_checkpoints_hangfire_job_id",
                table: "backfill_checkpoints",
                column: "hangfire_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_backfill_checkpoints_status",
                table: "backfill_checkpoints",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backfill_checkpoints");
        }
    }
}
