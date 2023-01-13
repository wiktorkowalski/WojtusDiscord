using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WojtusDiscord.ActivityArchiveService.Migrations
{
    public partial class CreateActivityArchiveDB : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discord_emotes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: true),
                    is_animated = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    discord_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_emotes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discord_presence_status_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    details = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: true),
                    large_image_text = table.Column<string>(type: "text", nullable: true),
                    small_image_text = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    activity_type = table.Column<string>(type: "text", nullable: false),
                    is_before = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_presence_status_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discord_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "text", nullable: false),
                    discriminator = table.Column<string>(type: "text", nullable: false),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    is_bot = table.Column<bool>(type: "boolean", nullable: false),
                    is_webhook = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    discord_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discord_voice_status_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_self_muted = table.Column<bool>(type: "boolean", nullable: false),
                    is_self_deafened = table.Column<bool>(type: "boolean", nullable: false),
                    is_self_stream = table.Column<bool>(type: "boolean", nullable: false),
                    is_self_video = table.Column<bool>(type: "boolean", nullable: false),
                    is_server_muted = table.Column<bool>(type: "boolean", nullable: false),
                    is_server_deafened = table.Column<bool>(type: "boolean", nullable: false),
                    is_suppressed = table.Column<bool>(type: "boolean", nullable: false),
                    is_before = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_voice_status_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discord_guilds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    icon_url = table.Column<string>(type: "text", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    discord_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_guilds", x => x.id);
                    table.ForeignKey(
                        name: "fk_discord_guilds_discord_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "discord_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discord_presence_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    before_id = table.Column<Guid>(type: "uuid", nullable: false),
                    after_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_presence_statuses", x => x.id);
                    table.ForeignKey(
                        name: "fk_discord_presence_statuses_discord_presence_status_entries_a",
                        column: x => x.after_id,
                        principalTable: "discord_presence_status_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discord_presence_statuses_discord_presence_status_entries_b",
                        column: x => x.before_id,
                        principalTable: "discord_presence_status_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discord_presence_statuses_discord_users_user_id",
                        column: x => x.user_id,
                        principalTable: "discord_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discord_guild_members",
                columns: table => new
                {
                    discord_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discord_guild_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_guild_members", x => new { x.discord_guild_id, x.discord_user_id });
                    table.ForeignKey(
                        name: "fk_discord_guild_members_discord_guilds_discord_guild_id",
                        column: x => x.discord_guild_id,
                        principalTable: "discord_guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discord_guild_members_discord_users_discord_user_id",
                        column: x => x.discord_user_id,
                        principalTable: "discord_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discord_text_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    topic = table.Column<string>(type: "text", nullable: true),
                    is_private = table.Column<bool>(type: "boolean", nullable: false),
                    is_thread = table.Column<bool>(type: "boolean", nullable: false),
                    parent_text_channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    discord_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_text_channels", x => x.id);
                    table.ForeignKey(
                        name: "fk_discord_text_channels_discord_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "discord_guilds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_discord_text_channels_discord_text_channels_parent_text_cha",
                        column: x => x.parent_text_channel_id,
                        principalTable: "discord_text_channels",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "discord_voice_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    bit_rate = table.Column<int>(type: "integer", nullable: false),
                    user_limit = table.Column<int>(type: "integer", nullable: false),
                    rtc_region = table.Column<string>(type: "text", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    discord_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_voice_channels", x => x.id);
                    table.ForeignKey(
                        name: "fk_discord_voice_channels_discord_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "discord_guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discord_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    has_attatchment = table.Column<bool>(type: "boolean", nullable: false),
                    is_edited = table.Column<bool>(type: "boolean", nullable: false),
                    is_removed = table.Column<bool>(type: "boolean", nullable: false),
                    discord_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reply_to_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    text_channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    discord_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_discord_messages_discord_messages_reply_to_message_id",
                        column: x => x.reply_to_message_id,
                        principalTable: "discord_messages",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_discord_messages_discord_text_channels_text_channel_id",
                        column: x => x.text_channel_id,
                        principalTable: "discord_text_channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discord_messages_discord_users_author_id",
                        column: x => x.author_id,
                        principalTable: "discord_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discord_typing_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text_channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_typing_statuses", x => x.id);
                    table.ForeignKey(
                        name: "fk_discord_typing_statuses_discord_text_channels_text_channel_id",
                        column: x => x.text_channel_id,
                        principalTable: "discord_text_channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discord_typing_statuses_discord_users_user_id",
                        column: x => x.user_id,
                        principalTable: "discord_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discord_voice_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    before_id = table.Column<Guid>(type: "uuid", nullable: false),
                    after_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voice_channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_voice_statuses", x => x.id);
                    table.ForeignKey(
                        name: "fk_discord_voice_statuses_discord_users_user_id",
                        column: x => x.user_id,
                        principalTable: "discord_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discord_voice_statuses_discord_voice_channels_voice_channel",
                        column: x => x.voice_channel_id,
                        principalTable: "discord_voice_channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discord_voice_statuses_discord_voice_status_entries_after_id",
                        column: x => x.after_id,
                        principalTable: "discord_voice_status_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discord_voice_statuses_discord_voice_status_entries_before_",
                        column: x => x.before_id,
                        principalTable: "discord_voice_status_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discord_message_content_edit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    content_before = table.Column<string>(type: "text", nullable: true),
                    is_removed = table.Column<bool>(type: "boolean", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_message_content_edit", x => x.id);
                    table.ForeignKey(
                        name: "fk_discord_message_content_edit_discord_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "discord_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discord_reactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_removed = table.Column<bool>(type: "boolean", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_reactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_discord_reactions_discord_emotes_emote_id",
                        column: x => x.emote_id,
                        principalTable: "discord_emotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discord_reactions_discord_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "discord_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discord_reactions_discord_users_user_id",
                        column: x => x.user_id,
                        principalTable: "discord_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discord_guild_members_discord_user_id",
                table: "discord_guild_members",
                column: "discord_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_guilds_owner_id",
                table: "discord_guilds",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_message_content_edit_message_id",
                table: "discord_message_content_edit",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_messages_author_id",
                table: "discord_messages",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_messages_reply_to_message_id",
                table: "discord_messages",
                column: "reply_to_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_messages_text_channel_id",
                table: "discord_messages",
                column: "text_channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_presence_statuses_after_id",
                table: "discord_presence_statuses",
                column: "after_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_presence_statuses_before_id",
                table: "discord_presence_statuses",
                column: "before_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_presence_statuses_user_id",
                table: "discord_presence_statuses",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_reactions_emote_id",
                table: "discord_reactions",
                column: "emote_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_reactions_message_id",
                table: "discord_reactions",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_reactions_user_id",
                table: "discord_reactions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_text_channels_guild_id",
                table: "discord_text_channels",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_text_channels_parent_text_channel_id",
                table: "discord_text_channels",
                column: "parent_text_channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_typing_statuses_text_channel_id",
                table: "discord_typing_statuses",
                column: "text_channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_typing_statuses_user_id",
                table: "discord_typing_statuses",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_voice_channels_guild_id",
                table: "discord_voice_channels",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_voice_statuses_after_id",
                table: "discord_voice_statuses",
                column: "after_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_voice_statuses_before_id",
                table: "discord_voice_statuses",
                column: "before_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_voice_statuses_user_id",
                table: "discord_voice_statuses",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_discord_voice_statuses_voice_channel_id",
                table: "discord_voice_statuses",
                column: "voice_channel_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discord_guild_members");

            migrationBuilder.DropTable(
                name: "discord_message_content_edit");

            migrationBuilder.DropTable(
                name: "discord_presence_statuses");

            migrationBuilder.DropTable(
                name: "discord_reactions");

            migrationBuilder.DropTable(
                name: "discord_typing_statuses");

            migrationBuilder.DropTable(
                name: "discord_voice_statuses");

            migrationBuilder.DropTable(
                name: "discord_presence_status_entries");

            migrationBuilder.DropTable(
                name: "discord_emotes");

            migrationBuilder.DropTable(
                name: "discord_messages");

            migrationBuilder.DropTable(
                name: "discord_voice_channels");

            migrationBuilder.DropTable(
                name: "discord_voice_status_entries");

            migrationBuilder.DropTable(
                name: "discord_text_channels");

            migrationBuilder.DropTable(
                name: "discord_guilds");

            migrationBuilder.DropTable(
                name: "discord_users");
        }
    }
}
