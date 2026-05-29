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
    }
}
