using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class StickerEntityConfiguration : IEntityTypeConfiguration<StickerEntity>
{
    public void Configure(EntityTypeBuilder<StickerEntity> builder)
    {
        builder.HasIndex(s => s.DiscordId).IsUnique();
        builder.HasIndex(s => s.GuildId);
        builder.ToTable(t => t.HasCheckConstraint("ck_stickers_soft_delete", SoftDeleteConstraint.Check));
    }
}
