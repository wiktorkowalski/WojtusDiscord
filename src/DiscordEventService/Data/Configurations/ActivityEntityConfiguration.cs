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

        builder.Property(a => a.Name).HasMaxLength(256);
        builder.Property(a => a.Details).HasMaxLength(1024);
        builder.Property(a => a.State).HasMaxLength(1024);
        builder.Property(a => a.LargeImageUrl).HasMaxLength(512);
        builder.Property(a => a.LargeImageText).HasMaxLength(256);
        builder.Property(a => a.SmallImageUrl).HasMaxLength(512);
        builder.Property(a => a.SmallImageText).HasMaxLength(256);
        builder.Property(a => a.PartyId).HasMaxLength(256);
        builder.Property(a => a.SpotifyTrackId).HasMaxLength(256);
        builder.Property(a => a.SpotifyAlbumArtUrl).HasMaxLength(512);
        builder.Property(a => a.SpotifyAlbumTitle).HasMaxLength(256);
        builder.Property(a => a.SpotifySongTitle).HasMaxLength(256);
        builder.Property(a => a.StreamUrl).HasMaxLength(512);
        builder.Property(a => a.CustomStatusEmojiName).HasMaxLength(256);

        builder.HasOne(a => a.User).WithMany(u => u.Activities)
            .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(a => a.Guild).WithMany(g => g.Activities)
            .HasForeignKey(a => a.GuildId).OnDelete(DeleteBehavior.NoAction);

        // Lifecycle-fact invariant (CONTEXT.md): stopping an activity is its natural end, so
        // is_active=false ⇔ ended_at_utc IS NOT NULL. DB-enforced like the soft-delete convention.
        builder.ToTable(t => t.HasCheckConstraint("ck_activities_lifecycle", LifecycleFactConstraint.Check("ended_at_utc")));
    }
}
