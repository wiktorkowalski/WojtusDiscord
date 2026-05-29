using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class GuildMembersChunkEventEntityConfiguration : IEntityTypeConfiguration<GuildMembersChunkEventEntity>
{
    public void Configure(EntityTypeBuilder<GuildMembersChunkEventEntity> builder)
    {
        builder.HasIndex(m => new { m.GuildDiscordId, m.EventTimestampUtc });

        builder.Property(m => m.MemberIdsJson).HasColumnType("jsonb");
        builder.Property(m => m.PresencesJson).HasColumnType("jsonb");
        builder.Property(m => m.NotFoundIdsJson).HasColumnType("jsonb");
    }
}
