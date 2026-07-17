using DiscordEventService.Data.Entities.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class ConversationEntityConfiguration : IEntityTypeConfiguration<ConversationEntity>
{
    public void Configure(EntityTypeBuilder<ConversationEntity> builder)
    {
        builder.ToTable("conversation");

        // The conversation key: one conversation per thread / DM channel.
        builder.HasIndex(c => c.ChannelDiscordId).IsUnique();
    }
}
