using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class MemeIndexEntityConfiguration : IEntityTypeConfiguration<MemeIndexEntity>
{
    // Status is the per-row lifecycle (#221 job): Failed/Skipped rows must say why.
    // "Indexed has an annotation" is a cross-table rule (#367) and stays in code.
    private const string StatusConstraintSql =
        "status IN (0, 1) OR (status IN (2, 3) AND error IS NOT NULL)";

    public void Configure(EntityTypeBuilder<MemeIndexEntity> builder)
    {
        builder.ToTable("meme_index", t => t.HasCheckConstraint("ck_meme_index_status", StatusConstraintSql));

        builder.HasIndex(m => m.AttachmentDiscordId).IsUnique();
        builder.HasIndex(m => m.ContentHash);
        builder.HasIndex(m => m.MessageId);

        // Same rationale as messages (§P2.6): meme rows must not vanish if a
        // message hard-delete ever happens — surface it as a violation instead.
        builder.HasOne(m => m.Message)
            .WithMany()
            .HasForeignKey(m => m.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
