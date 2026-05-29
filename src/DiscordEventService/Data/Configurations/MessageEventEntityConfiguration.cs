using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class MessageEventEntityConfiguration : IEntityTypeConfiguration<MessageEventEntity>
{
    public void Configure(EntityTypeBuilder<MessageEventEntity> builder)
    {
        builder.HasIndex(m => new { m.GuildDiscordId, m.EventTimestampUtc });
        builder.HasIndex(m => m.ChannelDiscordId);
        builder.HasIndex(m => m.AuthorDiscordId);
        builder.HasIndex(m => m.MessageDiscordId);

        builder.Property(m => m.AttachmentsJson).HasColumnType("jsonb");
        builder.Property(m => m.EmbedsJson).HasColumnType("jsonb");
    }
}
