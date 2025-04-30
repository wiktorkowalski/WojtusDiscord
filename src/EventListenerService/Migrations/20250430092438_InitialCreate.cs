using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventListenerService.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_emoji",
                columns: table => new
                {
                    ID = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Emoji = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_emoji", x => x.ID);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_emoji");
        }
    }
}
