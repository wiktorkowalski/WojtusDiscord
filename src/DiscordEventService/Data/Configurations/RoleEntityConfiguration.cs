using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class RoleEntityConfiguration : IEntityTypeConfiguration<RoleEntity>
{
    public void Configure(EntityTypeBuilder<RoleEntity> builder)
    {
        builder.HasIndex(r => r.DiscordId).IsUnique();
        builder.HasIndex(r => r.GuildId);
        builder.ToTable(t => t.HasCheckConstraint("ck_roles_soft_delete", SoftDeleteConstraint.Check));
    }
}
