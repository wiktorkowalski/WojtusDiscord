using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class AutoModRuleEntityConfiguration : IEntityTypeConfiguration<AutoModRuleEntity>
{
    public void Configure(EntityTypeBuilder<AutoModRuleEntity> builder)
    {
        builder.HasIndex(a => a.DiscordId).IsUnique();
        builder.HasIndex(a => a.GuildId);

        builder.Property(a => a.TriggerMetadataJson).HasColumnType("jsonb");
        builder.Property(a => a.ActionsJson).HasColumnType("jsonb");
        builder.Property(a => a.ExemptRolesJson).HasColumnType("jsonb");
        builder.Property(a => a.ExemptChannelsJson).HasColumnType("jsonb");

        builder.ToTable(t => t.HasCheckConstraint("ck_auto_mod_rules_soft_delete", SoftDeleteConstraint.Check));
    }
}
