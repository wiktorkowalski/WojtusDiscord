using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class MemeIndexRefusalMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "refused_by_model_id",
                table: "meme_index",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refused_by_prompt_version",
                table: "meme_index",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refused_by_model_id",
                table: "meme_index");

            migrationBuilder.DropColumn(
                name: "refused_by_prompt_version",
                table: "meme_index");
        }
    }
}
