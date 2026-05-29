using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class EmoteEntityConfiguration : IEntityTypeConfiguration<EmoteEntity>
{
    public void Configure(EntityTypeBuilder<EmoteEntity> builder)
    {
        builder.HasIndex(e => e.DiscordId).IsUnique();
        builder.HasIndex(e => e.GuildId);
        builder.ToTable(t => t.HasCheckConstraint("ck_emotes_soft_delete", SoftDeleteConstraint.Check));
    }
}
