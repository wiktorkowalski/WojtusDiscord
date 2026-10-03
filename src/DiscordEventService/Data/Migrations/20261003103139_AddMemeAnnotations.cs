using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemeAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Order matters: create and fill meme_annotations first, then drop from meme_index.
            // The generated columns and the check constraint go before the columns they read.
            migrationBuilder.CreateTable(
                name: "meme_annotations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    meme_index_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachment_discord_id = table.Column<long>(type: "bigint", nullable: false),
                    model_id = table.Column<string>(type: "text", nullable: false),
                    prompt_version = table.Column<string>(type: "text", nullable: false),
                    reasoning_effort = table.Column<string>(type: "text", nullable: true),
                    indexed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    description_pl = table.Column<string>(type: "text", nullable: false),
                    description_en = table.Column<string>(type: "text", nullable: false),
                    ocr_text = table.Column<string>(type: "text", nullable: false),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    source = table.Column<string>(type: "text", nullable: true),
                    template = table.Column<string>(type: "text", nullable: true),
                    raw_response_json = table.Column<string>(type: "jsonb", nullable: true),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false, computedColumnSql: "setweight(to_tsvector('simple', public.f_unaccent(coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(source, '') || ' ' || coalesce(template, ''))), 'A') || setweight(to_tsvector('simple', public.f_unaccent(coalesce(ocr_text, ''))), 'B') || setweight(to_tsvector('simple', public.f_unaccent(coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))), 'C')", stored: true),
                    search_text = table.Column<string>(type: "text", nullable: false, computedColumnSql: "public.f_unaccent(coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(source, '') || ' ' || coalesce(template, '') || ' ' || coalesce(ocr_text, '') || ' ' || coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meme_annotations", x => x.id);
                    table.ForeignKey(
                        name: "fk_meme_annotations_meme_index_meme_index_id",
                        column: x => x.meme_index_id,
                        principalTable: "meme_index",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Lossless (#367): every Indexed row becomes one annotation before its columns are
            // dropped. The old check constraint guarantees the copied columns are non-null for
            // status = 1. 'legacy' = written before prompt versions existed (v1 or v2, unknown).
            migrationBuilder.Sql("""
                INSERT INTO meme_annotations (
                    meme_index_id, attachment_discord_id, model_id, prompt_version, reasoning_effort, indexed_at_utc,
                    description_pl, description_en, ocr_text, tags, source, template, raw_response_json,
                    first_seen_utc, last_updated_utc)
                SELECT id, attachment_discord_id, model_id, 'legacy', NULL, indexed_at_utc,
                    description_pl, description_en, ocr_text, tags, source, template, raw_response_json,
                    now(), now()
                FROM meme_index
                WHERE status = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_meme_annotations_key",
                table: "meme_annotations",
                columns: new[] { "attachment_discord_id", "model_id", "prompt_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meme_annotations_meme_index_id",
                table: "meme_annotations",
                column: "meme_index_id");

            migrationBuilder.CreateIndex(
                name: "ix_meme_annotations_search_text",
                table: "meme_annotations",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_meme_annotations_search_vector",
                table: "meme_annotations",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.DropIndex(
                name: "ix_meme_index_search_text",
                table: "meme_index");

            migrationBuilder.DropIndex(
                name: "ix_meme_index_search_vector",
                table: "meme_index");

            migrationBuilder.DropCheckConstraint(
                name: "ck_meme_index_status",
                table: "meme_index");

            migrationBuilder.DropColumn(
                name: "search_text",
                table: "meme_index");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "meme_index");

            migrationBuilder.DropColumn(
                name: "description_en",
                table: "meme_index");

            migrationBuilder.DropColumn(
                name: "description_pl",
                table: "meme_index");

            migrationBuilder.DropColumn(
                name: "indexed_at_utc",
                table: "meme_index");

            migrationBuilder.DropColumn(
                name: "model_id",
                table: "meme_index");

            migrationBuilder.DropColumn(
                name: "ocr_text",
                table: "meme_index");

            migrationBuilder.DropColumn(
                name: "raw_response_json",
                table: "meme_index");

            migrationBuilder.DropColumn(
                name: "source",
                table: "meme_index");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "meme_index");

            migrationBuilder.DropColumn(
                name: "template",
                table: "meme_index");

            migrationBuilder.AddCheckConstraint(
                name: "ck_meme_index_status",
                table: "meme_index",
                sql: "status IN (0, 1) OR (status IN (2, 3) AND error IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_meme_index_status",
                table: "meme_index");

            migrationBuilder.AddColumn<string>(
                name: "description_en",
                table: "meme_index",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description_pl",
                table: "meme_index",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "indexed_at_utc",
                table: "meme_index",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "model_id",
                table: "meme_index",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ocr_text",
                table: "meme_index",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "raw_response_json",
                table: "meme_index",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "meme_index",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "tags",
                table: "meme_index",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "template",
                table: "meme_index",
                type: "text",
                nullable: true);

            // Lossy by construction: meme_index holds one metadata block per attachment, so only
            // the newest annotation of an Indexed row comes back. Its prompt version, reasoning
            // effort and every other annotation are gone.
            migrationBuilder.Sql("""
                UPDATE meme_index AS m
                SET description_pl = a.description_pl,
                    description_en = a.description_en,
                    ocr_text = a.ocr_text,
                    tags = a.tags,
                    source = a.source,
                    template = a.template,
                    model_id = a.model_id,
                    raw_response_json = a.raw_response_json,
                    indexed_at_utc = a.indexed_at_utc
                FROM (
                    SELECT DISTINCT ON (meme_index_id) *
                    FROM meme_annotations
                    ORDER BY meme_index_id, indexed_at_utc DESC, id DESC
                ) AS a
                WHERE a.meme_index_id = m.id AND m.status = 1;
                """);

            // The old check constraint rejects an Indexed row without metadata.
            migrationBuilder.Sql("UPDATE meme_index SET status = 0 WHERE status = 1 AND indexed_at_utc IS NULL;");

            migrationBuilder.DropTable(
                name: "meme_annotations");

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                table: "meme_index",
                type: "text",
                nullable: false,
                computedColumnSql: "public.f_unaccent(coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(source, '') || ' ' || coalesce(template, '') || ' ' || coalesce(ocr_text, '') || ' ' || coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "meme_index",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('simple', public.f_unaccent(coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(source, '') || ' ' || coalesce(template, ''))), 'A') || setweight(to_tsvector('simple', public.f_unaccent(coalesce(ocr_text, ''))), 'B') || setweight(to_tsvector('simple', public.f_unaccent(coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))), 'C')",
                stored: true);

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

            migrationBuilder.AddCheckConstraint(
                name: "ck_meme_index_status",
                table: "meme_index",
                sql: "(status = 0 AND indexed_at_utc IS NULL) OR (status = 1 AND indexed_at_utc IS NOT NULL AND model_id IS NOT NULL AND description_pl IS NOT NULL AND description_en IS NOT NULL AND ocr_text IS NOT NULL) OR (status = 2 AND error IS NOT NULL) OR (status = 3 AND error IS NOT NULL)");
        }
    }
}
