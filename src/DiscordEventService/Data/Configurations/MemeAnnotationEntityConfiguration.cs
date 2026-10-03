using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class MemeAnnotationEntityConfiguration : IEntityTypeConfiguration<MemeAnnotationEntity>
{
    // Weighted search vector (#220 binding design): tag/source/template hit ≫
    // OCR hit ≫ description hit under ts_rank's default weights. f_unaccent and
    // f_text_array_join are IMMUTABLE wrappers created in the MemeIndexSchema
    // migration — bare unaccent() and array_to_string() are STABLE and rejected
    // inside generated columns.
    private const string SearchVectorSql =
        "setweight(to_tsvector('simple', public.f_unaccent(coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(source, '') || ' ' || coalesce(template, ''))), 'A') || " +
        "setweight(to_tsvector('simple', public.f_unaccent(coalesce(ocr_text, ''))), 'B') || " +
        "setweight(to_tsvector('simple', public.f_unaccent(coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))), 'C')";

    private const string SearchTextSql =
        "public.f_unaccent(coalesce(public.f_text_array_join(tags), '') || ' ' || coalesce(source, '') || ' ' || coalesce(template, '') || ' ' || " +
        "coalesce(ocr_text, '') || ' ' || coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))";

    public void Configure(EntityTypeBuilder<MemeAnnotationEntity> builder)
    {
        builder.ToTable("meme_annotations");

        // The annotation key. Named by hand: the generated name is over Postgres' 63 characters.
        builder.HasIndex(a => new { a.AttachmentDiscordId, a.ModelId, a.PromptVersion })
            .IsUnique()
            .HasDatabaseName("ix_meme_annotations_key");

        builder.Property(a => a.RawResponseJson).HasColumnType("jsonb");

        builder.Property(a => a.SearchVector)
            .HasComputedColumnSql(SearchVectorSql, stored: true);
        builder.Property(a => a.SearchText)
            .HasComputedColumnSql(SearchTextSql, stored: true);

        builder.HasIndex(a => a.SearchVector).HasMethod("GIN");
        builder.HasIndex(a => a.SearchText).HasMethod("GIN").HasOperators("gin_trgm_ops");

        // Restrict, like meme_index → messages: annotations must not vanish with a status row.
        builder.HasOne(a => a.MemeIndex)
            .WithMany(m => m.Annotations)
            .HasForeignKey(a => a.MemeIndexId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
