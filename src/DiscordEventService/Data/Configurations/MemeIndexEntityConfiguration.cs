using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class MemeIndexEntityConfiguration : IEntityTypeConfiguration<MemeIndexEntity>
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

    // Status is the per-row lifecycle (#221 job): Pending rows carry no result
    // yet, Indexed rows must carry the full metadata block + provenance, and
    // Failed/Skipped rows must say why.
    private const string StatusConstraintSql =
        "(status = 0 AND indexed_at_utc IS NULL) OR " +
        "(status = 1 AND indexed_at_utc IS NOT NULL AND model_id IS NOT NULL AND description_pl IS NOT NULL AND description_en IS NOT NULL AND ocr_text IS NOT NULL) OR " +
        "(status = 2 AND error IS NOT NULL) OR " +
        "(status = 3 AND error IS NOT NULL)";

    public void Configure(EntityTypeBuilder<MemeIndexEntity> builder)
    {
        builder.ToTable("meme_index", t => t.HasCheckConstraint("ck_meme_index_status", StatusConstraintSql));

        builder.HasIndex(m => m.AttachmentDiscordId).IsUnique();
        builder.HasIndex(m => m.ContentHash);
        builder.HasIndex(m => m.MessageId);

        builder.Property(m => m.RawResponseJson).HasColumnType("jsonb");

        builder.Property(m => m.SearchVector)
            .HasComputedColumnSql(SearchVectorSql, stored: true);
        builder.Property(m => m.SearchText)
            .HasComputedColumnSql(SearchTextSql, stored: true);

        builder.HasIndex(m => m.SearchVector).HasMethod("GIN");
        builder.HasIndex(m => m.SearchText).HasMethod("GIN").HasOperators("gin_trgm_ops");

        // Same rationale as messages (§P2.6): meme rows must not vanish if a
        // message hard-delete ever happens — surface it as a violation instead.
        builder.HasOne(m => m.Message)
            .WithMany()
            .HasForeignKey(m => m.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
