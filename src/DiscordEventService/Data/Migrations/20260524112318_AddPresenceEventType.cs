using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPresenceEventType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "event_type",
                table: "presence_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "event_type",
                table: "presence_events");
        }
    }
}
