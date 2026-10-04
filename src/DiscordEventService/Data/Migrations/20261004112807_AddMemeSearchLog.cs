using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemeSearchLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "meme_search_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    searched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    user_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    query = table.Column<string>(type: "text", nullable: false),
                    tokens = table.Column<string[]>(type: "text[]", nullable: false),
                    rank_tokens = table.Column<string[]>(type: "text[]", nullable: false),
                    result_limit = table.Column<int>(type: "integer", nullable: false),
                    result_count = table.Column<int>(type: "integer", nullable: false),
                    zero_results = table.Column<bool>(type: "boolean", nullable: false, computedColumnSql: "result_count = 0", stored: true),
                    duration_ms = table.Column<double>(type: "double precision", nullable: false),
                    trigram_weight = table.Column<double>(type: "double precision", nullable: false),
                    trigram_threshold = table.Column<double>(type: "double precision", nullable: false),
                    stop_list_version = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meme_search_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "meme_search_log_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    search_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    attachment_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    model_id = table.Column<string>(type: "text", nullable: false),
                    prompt_version = table.Column<string>(type: "text", nullable: false),
                    ts_rank = table.Column<double>(type: "double precision", nullable: false),
                    trigram_similarity = table.Column<double>(type: "double precision", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meme_search_log_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_meme_search_log_results_meme_search_log_search_id",
                        column: x => x.search_id,
                        principalTable: "meme_search_log",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_meme_search_log_searched_at_utc",
                table: "meme_search_log",
                column: "searched_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_meme_search_log_results_attachment_discord_id",
                table: "meme_search_log_results",
                column: "attachment_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_meme_search_log_results_search_id_rank",
                table: "meme_search_log_results",
                columns: new[] { "search_id", "rank" },
                unique: true);

            // The log holds what each person typed. The assistant's query_database role gets SELECT on
            // every new table through the default privileges of AddConversationQueryRole, and any member
            // can run that tool. Take the grant back: the owner reads this log with direct SQL only.
            // Guarded like that migration: a database without the role has no grant to remove.
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wojtus_query') THEN
                        REVOKE ALL ON TABLE meme_search_log, meme_search_log_results FROM wojtus_query;
                    END IF;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meme_search_log_results");

            migrationBuilder.DropTable(
                name: "meme_search_log");
        }
    }
}
