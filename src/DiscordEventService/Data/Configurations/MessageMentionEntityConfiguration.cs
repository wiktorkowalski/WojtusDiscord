using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class MessageMentionEntityConfiguration : IEntityTypeConfiguration<MessageMentionEntity>
{
    public void Configure(EntityTypeBuilder<MessageMentionEntity> builder)
    {
        builder.HasOne(m => m.Message)
            .WithMany()
            .HasForeignKey(m => m.MessageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.MessageId);
        builder.HasIndex(m => m.MentionedUserDiscordId);
        builder.HasIndex(m => m.MentionedRoleDiscordId);
        builder.HasIndex(m => m.MentionedChannelDiscordId);

        builder.ToTable(t => t.HasCheckConstraint("ck_message_mentions_target",
            "(mention_type = 0 AND mentioned_user_discord_id IS NOT NULL AND mentioned_role_discord_id IS NULL AND mentioned_channel_discord_id IS NULL) " +
            "OR (mention_type = 1 AND mentioned_role_discord_id IS NOT NULL AND mentioned_user_discord_id IS NULL AND mentioned_channel_discord_id IS NULL) " +
            "OR (mention_type IN (2,3) AND mentioned_user_discord_id IS NULL AND mentioned_role_discord_id IS NULL AND mentioned_channel_discord_id IS NULL) " +
            "OR (mention_type = 4 AND mentioned_channel_discord_id IS NOT NULL AND mentioned_user_discord_id IS NULL AND mentioned_role_discord_id IS NULL)"));
    }
}
