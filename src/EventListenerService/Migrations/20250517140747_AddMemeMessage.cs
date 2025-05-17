using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventListenerService.Migrations
{
    /// <inheritdoc />
    public partial class AddMemeMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "meme_messages",
                columns: table => new
                {
                    message_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channel_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    author_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    author_username = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    image_url_proxy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meme_messages", x => x.message_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meme_messages");
        }
    }
}
