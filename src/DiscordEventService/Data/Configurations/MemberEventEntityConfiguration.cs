using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class MemberEventEntityConfiguration : IEntityTypeConfiguration<MemberEventEntity>
{
    public void Configure(EntityTypeBuilder<MemberEventEntity> builder)
    {
        builder.HasIndex(m => new { m.GuildDiscordId, m.EventTimestampUtc });
        builder.HasIndex(m => m.UserDiscordId);

        builder.Property(m => m.RolesAddedJson).HasColumnType("jsonb");
        builder.Property(m => m.RolesRemovedJson).HasColumnType("jsonb");

        // Properties carry the Utc suffix convention; the columns predate it.
        builder.Property(m => m.PremiumSinceBeforeUtc).HasColumnName("premium_since_before");
        builder.Property(m => m.PremiumSinceAfterUtc).HasColumnName("premium_since_after");
    }
}
