using DiscordEventService.Data.Entities.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class ConversationMessageEntityConfiguration : IEntityTypeConfiguration<ConversationMessageEntity>
{
    // Role decides which columns a row carries: assistant rows may bundle tool calls,
    // tool rows must name the call they answer, user rows carry only text.
    private const string RoleShapeConstraintSql =
        "(role = 0 AND tool_calls_json IS NULL AND tool_call_id IS NULL AND tool_result IS NULL) OR " +
        "(role = 1 AND tool_call_id IS NULL AND tool_result IS NULL) OR " +
        "(role = 2 AND tool_call_id IS NOT NULL AND tool_result IS NOT NULL AND tool_calls_json IS NULL)";

    public void Configure(EntityTypeBuilder<ConversationMessageEntity> builder)
    {
        builder.ToTable("conversation_message",
            t => t.HasCheckConstraint("ck_conversation_message_role_shape", RoleShapeConstraintSql));

        // Window loads walk one conversation newest-first; the uuidv7 PK is the
        // insertion order within it.
        builder.HasIndex(m => new { m.ConversationId, m.Id });

        builder.Property(m => m.ToolCallsJson).HasColumnType("jsonb");

        // Append-only history — never cascade-delete it with the conversation row.
        builder.HasOne(m => m.Conversation)
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
