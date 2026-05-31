using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class ActivityEntityConfiguration : IEntityTypeConfiguration<ActivityEntity>
{
    public void Configure(EntityTypeBuilder<ActivityEntity> builder)
    {
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => new { a.UserId, a.IsActive });
        builder.HasIndex(a => a.ActivityType);
        builder.HasIndex(a => a.FirstSeenAtUtc);

        builder.Property(a => a.SpotifyArtistsJson).HasColumnType("jsonb");
        builder.Property(a => a.ButtonsJson).HasColumnType("jsonb");

        builder.HasOne(a => a.User).WithMany(u => u.Activities)
            .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(a => a.Guild).WithMany(g => g.Activities)
            .HasForeignKey(a => a.GuildId).OnDelete(DeleteBehavior.NoAction);

        // Lifecycle-fact invariant (CONTEXT.md): stopping an activity is its natural end, so
        // is_active=false ⇔ ended_at_utc IS NOT NULL. DB-enforced like the soft-delete convention.
        builder.ToTable(t => t.HasCheckConstraint("ck_activities_lifecycle", LifecycleFactConstraint.Check("ended_at_utc")));
    }
}
