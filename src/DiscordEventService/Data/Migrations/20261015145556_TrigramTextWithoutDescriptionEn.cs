using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrigramTextWithoutDescriptionEn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "search_text",
                table: "meme_annotations",
                type: "text",
                nullable: false,
                computedColumnSql: "public.f_unaccent(coalesce(public.f_text_array_join(templates), '') || ' ' || coalesce(public.f_text_array_join(search_phrases), '') || ' ' || coalesce(public.f_text_array_join(people_names), '') || ' ' || coalesce(franchise, '') || ' ' || coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(nullif(source, 'other'), '') || ' ' || coalesce(ocr_text, '') || ' ' || coalesce(description_pl, ''))",
                stored: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldComputedColumnSql: "public.f_unaccent(coalesce(public.f_text_array_join(templates), '') || ' ' || coalesce(public.f_text_array_join(search_phrases), '') || ' ' || coalesce(public.f_text_array_join(people_names), '') || ' ' || coalesce(franchise, '') || ' ' || coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(nullif(source, 'other'), '') || ' ' || coalesce(ocr_text, '') || ' ' || coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))",
                oldStored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "search_text",
                table: "meme_annotations",
                type: "text",
                nullable: false,
                computedColumnSql: "public.f_unaccent(coalesce(public.f_text_array_join(templates), '') || ' ' || coalesce(public.f_text_array_join(search_phrases), '') || ' ' || coalesce(public.f_text_array_join(people_names), '') || ' ' || coalesce(franchise, '') || ' ' || coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(nullif(source, 'other'), '') || ' ' || coalesce(ocr_text, '') || ' ' || coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))",
                stored: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldComputedColumnSql: "public.f_unaccent(coalesce(public.f_text_array_join(templates), '') || ' ' || coalesce(public.f_text_array_join(search_phrases), '') || ' ' || coalesce(public.f_text_array_join(people_names), '') || ' ' || coalesce(franchise, '') || ' ' || coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(nullif(source, 'other'), '') || ' ' || coalesce(ocr_text, '') || ' ' || coalesce(description_pl, ''))",
                oldStored: true);
        }
    }
}
