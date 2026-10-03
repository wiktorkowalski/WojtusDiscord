using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class MemeAnnotationEntityConfiguration : IEntityTypeConfiguration<MemeAnnotationEntity>
{
    // Weight A (#368): what a person types to find one meme — templates, search phrases, people,
    // franchise, tags, source. The "other" source is a bucket, not a name: left out, or every
    // query holding the word "other" would get a weight-A hit on it.
    private const string WeightASql =
        "coalesce(public.f_text_array_join(templates), '') || ' ' || " +
        "coalesce(public.f_text_array_join(search_phrases), '') || ' ' || " +
        "coalesce(public.f_text_array_join(people_names), '') || ' ' || " +
        "coalesce(franchise, '') || ' ' || " +
        "coalesce(public.f_text_array_join(tags), '') || ' ' || " +
        "coalesce(nullif(source, '" + MemeSources.Other + "'), '')";

    // Weighted search vector (#220 binding design): a weight-A hit ≫ OCR hit ≫ description hit
    // under ts_rank's default weights. f_unaccent and f_text_array_join are IMMUTABLE wrappers
    // created in the MemeIndexSchema migration — bare unaccent() and array_to_string() are
    // STABLE and rejected inside generated columns.
    private const string SearchVectorSql =
        "setweight(to_tsvector('simple', public.f_unaccent(" + WeightASql + ")), 'A') || " +
        "setweight(to_tsvector('simple', public.f_unaccent(coalesce(ocr_text, ''))), 'B') || " +
        "setweight(to_tsvector('simple', public.f_unaccent(coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))), 'C')";

    // Trigram side: the same fields as the vector, unweighted.
    private const string SearchTextSql =
        "public.f_unaccent(" + WeightASql + " || ' ' || " +
        "coalesce(ocr_text, '') || ' ' || coalesce(description_pl, '') || ' ' || coalesce(description_en, ''))";

    private const string EmptyTextArraySql = "'{}'::text[]";

    public void Configure(EntityTypeBuilder<MemeAnnotationEntity> builder)
    {
        builder.ToTable("meme_annotations",
            t => t.HasCheckConstraint("ck_meme_annotations_source", MemeSources.CheckConstraintSql));

        // The annotation key. Named by hand: the generated name is over Postgres' 63 characters.
        builder.HasIndex(a => new { a.AttachmentDiscordId, a.ModelId, a.PromptVersion })
            .IsUnique()
            .HasDatabaseName("ix_meme_annotations_key");

        builder.Property(a => a.RawResponseJson).HasColumnType("jsonb");

        // Database defaults: an annotation written before schema v2 reads as empty, not as NULL.
        builder.Property(a => a.Templates).HasDefaultValueSql(EmptyTextArraySql);
        builder.Property(a => a.SearchPhrases).HasDefaultValueSql(EmptyTextArraySql);
        builder.Property(a => a.PeopleNames).HasDefaultValueSql(EmptyTextArraySql);
        builder.Property(a => a.People).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");

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
