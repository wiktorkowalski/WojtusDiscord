using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationMemoryAndUsageLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_activity_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_message",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    turn_index = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: true),
                    tool_calls_json = table.Column<string>(type: "jsonb", nullable: true),
                    tool_call_id = table.Column<string>(type: "text", nullable: true),
                    tool_name = table.Column<string>(type: "text", nullable: true),
                    tool_result = table.Column<string>(type: "text", nullable: true),
                    reasoning = table.Column<string>(type: "text", nullable: true),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    est_tokens = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_message", x => x.id);
                    table.CheckConstraint("ck_conversation_message_role_shape", "(role = 0 AND tool_calls_json IS NULL AND tool_call_id IS NULL AND tool_result IS NULL) OR (role = 1 AND tool_call_id IS NULL AND tool_result IS NULL) OR (role = 2 AND tool_call_id IS NOT NULL AND tool_result IS NOT NULL AND tool_calls_json IS NULL)");
                    table.ForeignKey(
                        name: "fk_conversation_message_conversation_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversation_usage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoker_id = table.Column<long>(type: "bigint", nullable: false),
                    turn_index = table.Column<int>(type: "integer", nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    cost_usd = table.Column<double>(type: "double precision", nullable: true),
                    upstream_inference_cost_usd = table.Column<double>(type: "double precision", nullable: true),
                    web_search_requests = table.Column<int>(type: "integer", nullable: true),
                    latency_ms = table.Column<long>(type: "bigint", nullable: false),
                    failed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_usage", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_usage_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_channel_discord_id",
                table: "conversation",
                column: "channel_discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_conversation_id_id",
                table: "conversation_message",
                columns: new[] { "conversation_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_usage_conversation_id",
                table: "conversation_usage",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_usage_invoker_id_created_at_utc",
                table: "conversation_usage",
                columns: new[] { "invoker_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_message");

            migrationBuilder.DropTable(
                name: "conversation_usage");

            migrationBuilder.DropTable(
                name: "conversation");
        }
    }
}
