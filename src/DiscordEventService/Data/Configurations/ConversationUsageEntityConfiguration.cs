using DiscordEventService.Data.Entities.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class ConversationUsageEntityConfiguration : IEntityTypeConfiguration<ConversationUsageEntity>
{
    public void Configure(EntityTypeBuilder<ConversationUsageEntity> builder)
    {
        builder.ToTable("conversation_usage");

        // "Cost per user this month" is a single WHERE over these two (#256).
        builder.HasIndex(u => new { u.InvokerId, u.CreatedAtUtc });
        builder.HasIndex(u => u.ConversationId);

        builder.HasOne(u => u.Conversation)
            .WithMany()
            .HasForeignKey(u => u.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
