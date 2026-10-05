using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class MemeSearchLogEntityConfiguration : IEntityTypeConfiguration<MemeSearchLogEntity>
{
    public void Configure(EntityTypeBuilder<MemeSearchLogEntity> builder)
    {
        builder.ToTable("meme_search_log");

        // "The latest searches" is the one query this table exists for.
        builder.HasIndex(s => s.SearchedAtUtc);

        // Keeps the model equal to the column that AddMemeSearchLogResultOffset created (#391):
        // the default in its AddColumn is what gave the rows from before paging their 0.
        builder.Property(s => s.ResultOffset).HasDefaultValue(0);

        builder.Property(s => s.ZeroResults)
            .HasComputedColumnSql("result_count = 0", stored: true);
    }
}

internal sealed class MemeSearchLogResultEntityConfiguration : IEntityTypeConfiguration<MemeSearchLogResultEntity>
{
    public void Configure(EntityTypeBuilder<MemeSearchLogResultEntity> builder)
    {
        builder.ToTable("meme_search_log_results");

        builder.HasIndex(r => new { r.SearchId, r.Rank }).IsUnique();

        // "Which searches returned this meme".
        builder.HasIndex(r => r.AttachmentDiscordId);

        // Cascade, unlike the Restrict of the other meme tables: a hit has no meaning without its search,
        // and a later retention delete has to remove both.
        builder.HasOne(r => r.Search)
            .WithMany(s => s.Results)
            .HasForeignKey(r => r.SearchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
