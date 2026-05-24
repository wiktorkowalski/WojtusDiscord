using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class P3_2_MessageMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_mentions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mentioned_user_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    mentioned_role_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    mentioned_channel_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    mention_type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_mentions", x => x.id);
                    table.CheckConstraint("ck_message_mentions_target", "(mention_type = 0 AND mentioned_user_discord_id IS NOT NULL AND mentioned_role_discord_id IS NULL AND mentioned_channel_discord_id IS NULL) OR (mention_type = 1 AND mentioned_role_discord_id IS NOT NULL AND mentioned_user_discord_id IS NULL AND mentioned_channel_discord_id IS NULL) OR (mention_type IN (2,3) AND mentioned_user_discord_id IS NULL AND mentioned_role_discord_id IS NULL AND mentioned_channel_discord_id IS NULL) OR (mention_type = 4 AND mentioned_channel_discord_id IS NOT NULL AND mentioned_user_discord_id IS NULL AND mentioned_role_discord_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_message_mentions_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_message_mentions_mentioned_channel_discord_id",
                table: "message_mentions",
                column: "mentioned_channel_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_mentions_mentioned_role_discord_id",
                table: "message_mentions",
                column: "mentioned_role_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_mentions_mentioned_user_discord_id",
                table: "message_mentions",
                column: "mentioned_user_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_mentions_message_id",
                table: "message_mentions",
                column: "message_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_mentions");
        }
    }
}
