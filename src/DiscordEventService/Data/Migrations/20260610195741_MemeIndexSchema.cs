using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class MemeIndexSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            // Bare unaccent() and array_to_string() are STABLE (unaccent's
            // dictionary resolves via search_path; array_to_string invokes
            // element output functions) and PostgreSQL rejects non-IMMUTABLE
            // functions in generated columns. With a pinned dictionary and pure
            // text[] input both are deterministic, hence safely IMMUTABLE.
            // Must exist before CreateTable - meme_index's generated columns call them.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.f_unaccent(text)
                  RETURNS text
                  LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                AS $func$
                SELECT public.unaccent('public.unaccent'::regdictionary, $1)
                $func$;

                CREATE OR REPLACE FUNCTION public.f_text_array_join(text[])
                  RETURNS text
                  LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                AS $func$
                SELECT array_to_string($1, ' ')
                $func$;
                """);

            migrationBuilder.CreateTable(
                name: "meme_index",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    message_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    attachment_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: true),
                    content_hash = table.Column<string>(type: "text", nullable: true),
                    description_pl = table.Column<string>(type: "text", nullable: true),
                    description_en = table.Column<string>(type: "text", nullable: true),
                    ocr_text = table.Column<string>(type: "text", nullable: true),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    source = table.Column<string>(type: "text", nullable: true),
                    template = table.Column<string>(type: "text", nullable: true),
                    model_id = table.Column<string>(type: "text", nullable: true),
                    raw_response_json = table.Column<string>(type: "jsonb", nullable: true),
                    indexed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false, computedColumnSql: "setweight(to_tsvector('simple', public.f_unaccent(coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(source, '') || ' ' || coalesce(template, ''))), 'A') || setweight(to_tsvector('simple', public.f_unaccent(coalesce(ocr_text, ''))), 'B') || setweight(to_tsvector('simple', public.f_unaccent(coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))), 'C')", stored: true),
                    search_text = table.Column<string>(type: "text", nullable: false, computedColumnSql: "public.f_unaccent(coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(source, '') || ' ' || coalesce(template, '') || ' ' || coalesce(ocr_text, '') || ' ' || coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meme_index", x => x.id);
                    table.CheckConstraint("ck_meme_index_status", "(status = 0 AND indexed_at_utc IS NULL) OR (status = 1 AND indexed_at_utc IS NOT NULL AND model_id IS NOT NULL AND description_pl IS NOT NULL AND description_en IS NOT NULL AND ocr_text IS NOT NULL) OR (status = 2 AND error IS NOT NULL) OR (status = 3 AND error IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_meme_index_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_meme_index_attachment_discord_id",
                table: "meme_index",
                column: "attachment_discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meme_index_content_hash",
                table: "meme_index",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_meme_index_message_id",
                table: "meme_index",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_meme_index_search_text",
                table: "meme_index",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_meme_index_search_vector",
                table: "meme_index",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meme_index");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.f_unaccent(text); DROP FUNCTION IF EXISTS public.f_text_array_join(text[]);");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,");
        }
    }
}
