using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class P5_1_RawEventSerializationFailedFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "serialization_failed",
                table: "raw_event_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill the flag for pre-existing serialization-failure stubs so it is authoritative
            // for all rows (these were previously detectable only by matching the stub's error
            // string). The predicate is exact: the stub is the only payload with a top-level
            // "error" field equal to the marker. Flag-set only — no data loss.
            migrationBuilder.Sql(
                "UPDATE raw_event_logs SET serialization_failed = true " +
                "WHERE event_json->>'error' = 'Serialization failed';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "serialization_failed",
                table: "raw_event_logs");
        }
    }
}
