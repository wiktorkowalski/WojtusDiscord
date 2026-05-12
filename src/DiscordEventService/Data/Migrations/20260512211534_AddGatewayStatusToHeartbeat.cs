using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewayStatusToHeartbeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "gateway_latency_ms",
                table: "bot_heartbeats",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_gateway_connected",
                table: "bot_heartbeats",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "gateway_latency_ms",
                table: "bot_heartbeats");

            migrationBuilder.DropColumn(
                name: "is_gateway_connected",
                table: "bot_heartbeats");
        }
    }
}
