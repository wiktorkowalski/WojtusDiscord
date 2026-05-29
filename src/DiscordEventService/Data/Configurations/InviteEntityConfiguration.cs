using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class InviteEntityConfiguration : IEntityTypeConfiguration<InviteEntity>
{
    public void Configure(EntityTypeBuilder<InviteEntity> builder)
    {
        builder.HasIndex(i => i.Code).IsUnique();
        builder.HasIndex(i => i.GuildId);
        builder.HasIndex(i => new { i.GuildId, i.IsDeleted });
        builder.ToTable(t => t.HasCheckConstraint("ck_invites_soft_delete", SoftDeleteConstraint.Check));
    }
}
