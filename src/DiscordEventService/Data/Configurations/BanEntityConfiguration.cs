using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class BanEntityConfiguration : IEntityTypeConfiguration<BanEntity>
{
    public void Configure(EntityTypeBuilder<BanEntity> builder)
    {
        builder.HasIndex(b => b.GuildId);
        builder.HasIndex(b => new { b.GuildId, b.UserId });
        builder.HasIndex(b => new { b.GuildId, b.IsActive });

        // Lifecycle-fact invariant (CONTEXT.md): an unban is the natural end of the ban, so
        // is_active=false ⇔ unbanned_at_utc IS NOT NULL. DB-enforced like the soft-delete convention.
        builder.ToTable(t => t.HasCheckConstraint("ck_bans_lifecycle", LifecycleFactConstraint.Check("unbanned_at_utc")));
    }
}
