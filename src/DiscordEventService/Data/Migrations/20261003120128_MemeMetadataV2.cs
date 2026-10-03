using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class MemeMetadataV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-ordered (#368); the scaffold was wrong twice. It guessed RenameColumn
            // template -> franchise, which files every template name under franchise. And it
            // emitted AlterColumn for the generated columns, which Npgsql runs as drop + add:
            // Postgres drops the GIN indexes with the columns and nothing recreates them.
            // Order: new columns -> data -> old generated columns and their indexes -> template
            // (they read it) -> new generated columns and indexes -> check constraint.
            migrationBuilder.AddColumn<string>(
                name: "franchise",
                table: "meme_annotations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "image_kind",
                table: "meme_annotations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "language",
                table: "meme_annotations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "people",
                table: "meme_annotations",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string[]>(
                name: "people_names",
                table: "meme_annotations",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string[]>(
                name: "search_phrases",
                table: "meme_annotations",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string[]>(
                name: "templates",
                table: "meme_annotations",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            // Lossless: the one template becomes a one-element list. A blank one is no template.
            migrationBuilder.Sql("""
                UPDATE meme_annotations
                SET templates = ARRAY[template]
                WHERE template IS NOT NULL AND btrim(template) <> '';
                """);

            // source becomes a closed set (the list is frozen here: a migration must not follow
            // later edits of MemeSources.Known). 'none', the string 'null' and a blank mean no
            // source. Free text that names no known platform turns into 'other'; its words
            // move to tags first, so the row is still found by them.
            migrationBuilder.Sql("""
                UPDATE meme_annotations
                SET tags = array_append(tags, lower(btrim(source)))
                WHERE source IS NOT NULL
                  AND lower(btrim(source)) NOT IN ('reddit', 'twitter', 'facebook', 'instagram', 'tiktok', 'youtube', 'discord', 'kwejk', 'jbzd', 'jeja', 'wykop', 'demotywatory', 'blasty', 'memisko', 'imgflip', '9gag', 'ifunny', 'other', 'x', 'none', 'null', '')
                  AND array_position(tags, lower(btrim(source))) IS NULL;

                UPDATE meme_annotations
                SET source = CASE
                    WHEN lower(btrim(source)) IN ('reddit', 'twitter', 'facebook', 'instagram', 'tiktok', 'youtube', 'discord', 'kwejk', 'jbzd', 'jeja', 'wykop', 'demotywatory', 'blasty', 'memisko', 'imgflip', '9gag', 'ifunny', 'other') THEN lower(btrim(source))
                    WHEN lower(btrim(source)) = 'x' THEN 'twitter'
                    WHEN lower(btrim(source)) IN ('none', 'null', '') THEN NULL
                    ELSE 'other'
                END
                WHERE source IS NOT NULL;
                """);

            migrationBuilder.DropIndex(
                name: "ix_meme_annotations_search_text",
                table: "meme_annotations");

            migrationBuilder.DropIndex(
                name: "ix_meme_annotations_search_vector",
                table: "meme_annotations");

            migrationBuilder.DropColumn(
                name: "search_text",
                table: "meme_annotations");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "meme_annotations");

            migrationBuilder.DropColumn(
                name: "template",
                table: "meme_annotations");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "meme_annotations",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('simple', public.f_unaccent(coalesce(public.f_text_array_join(templates), '') || ' ' || coalesce(public.f_text_array_join(search_phrases), '') || ' ' || coalesce(public.f_text_array_join(people_names), '') || ' ' || coalesce(franchise, '') || ' ' || coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(nullif(source, 'other'), ''))), 'A') || setweight(to_tsvector('simple', public.f_unaccent(coalesce(ocr_text, ''))), 'B') || setweight(to_tsvector('simple', public.f_unaccent(coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))), 'C')",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                table: "meme_annotations",
                type: "text",
                nullable: false,
                computedColumnSql: "public.f_unaccent(coalesce(public.f_text_array_join(templates), '') || ' ' || coalesce(public.f_text_array_join(search_phrases), '') || ' ' || coalesce(public.f_text_array_join(people_names), '') || ' ' || coalesce(franchise, '') || ' ' || coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(nullif(source, 'other'), '') || ' ' || coalesce(ocr_text, '') || ' ' || coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))",
                stored: true);

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

            migrationBuilder.AddCheckConstraint(
                name: "ck_meme_annotations_source",
                table: "meme_annotations",
                sql: "source IS NULL OR source IN ('reddit', 'twitter', 'facebook', 'instagram', 'tiktok', 'youtube', 'discord', 'kwejk', 'jbzd', 'jeja', 'wykop', 'demotywatory', 'blasty', 'memisko', 'imgflip', '9gag', 'ifunny', 'other')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_meme_annotations_source",
                table: "meme_annotations");

            migrationBuilder.DropIndex(
                name: "ix_meme_annotations_search_text",
                table: "meme_annotations");

            migrationBuilder.DropIndex(
                name: "ix_meme_annotations_search_vector",
                table: "meme_annotations");

            migrationBuilder.DropColumn(
                name: "search_text",
                table: "meme_annotations");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "meme_annotations");

            migrationBuilder.AddColumn<string>(
                name: "template",
                table: "meme_annotations",
                type: "text",
                nullable: true);

            // Lossy by construction: one template column holds only the first of templates.
            // people, search_phrases, franchise, image_kind and language have no old column.
            // source keeps its mapped value, and a tag added by Up() stays.
            migrationBuilder.Sql("""
                UPDATE meme_annotations
                SET template = templates[1]
                WHERE cardinality(templates) > 0;
                """);

            migrationBuilder.DropColumn(
                name: "franchise",
                table: "meme_annotations");

            migrationBuilder.DropColumn(
                name: "image_kind",
                table: "meme_annotations");

            migrationBuilder.DropColumn(
                name: "language",
                table: "meme_annotations");

            migrationBuilder.DropColumn(
                name: "people",
                table: "meme_annotations");

            migrationBuilder.DropColumn(
                name: "people_names",
                table: "meme_annotations");

            migrationBuilder.DropColumn(
                name: "search_phrases",
                table: "meme_annotations");

            migrationBuilder.DropColumn(
                name: "templates",
                table: "meme_annotations");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "meme_annotations",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('simple', public.f_unaccent(coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(source, '') || ' ' || coalesce(template, ''))), 'A') || setweight(to_tsvector('simple', public.f_unaccent(coalesce(ocr_text, ''))), 'B') || setweight(to_tsvector('simple', public.f_unaccent(coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))), 'C')",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                table: "meme_annotations",
                type: "text",
                nullable: false,
                computedColumnSql: "public.f_unaccent(coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(source, '') || ' ' || coalesce(template, '') || ' ' || coalesce(ocr_text, '') || ' ' || coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))",
                stored: true);

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
        }
    }
}
