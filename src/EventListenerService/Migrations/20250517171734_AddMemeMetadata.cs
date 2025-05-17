using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventListenerService.Migrations
{
    /// <inheritdoc />
    public partial class AddMemeMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "meme_metadata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    text_content = table.Column<string>(type: "text", nullable: true),
                    keywords = table.Column<string[]>(type: "text[]", nullable: true),
                    objects = table.Column<string[]>(type: "text[]", nullable: true),
                    tone = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meme_metadata", x => x.id);
                    table.ForeignKey(
                        name: "fk_meme_metadata_meme_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "meme_messages",
                        principalColumn: "message_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_meme_metadata_message_id",
                table: "meme_metadata",
                column: "message_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meme_metadata");
        }
    }
}
