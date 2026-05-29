using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class IntegrationEntityConfiguration : IEntityTypeConfiguration<IntegrationEntity>
{
    public void Configure(EntityTypeBuilder<IntegrationEntity> builder)
    {
        builder.HasIndex(i => i.DiscordId).IsUnique();
        builder.HasIndex(i => i.GuildId);
        builder.ToTable(t => t.HasCheckConstraint("ck_integrations_soft_delete", SoftDeleteConstraint.Check));
    }
}
