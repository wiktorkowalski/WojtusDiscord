using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class MessageEditHistoryEntityConfiguration : IEntityTypeConfiguration<MessageEditHistoryEntity>
{
    public void Configure(EntityTypeBuilder<MessageEditHistoryEntity> builder)
    {
        builder.HasIndex(m => m.MessageDiscordId);
        builder.HasIndex(m => m.MessageId);
        builder.HasIndex(m => m.EditedAtUtc);

        builder.HasOne(e => e.Message).WithMany(m => m.EditHistory)
            .HasForeignKey(e => e.MessageId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // Match the jsonb column type used on messages.attachments_json /
        // messages.embeds_json so the history columns are queryable with the
        // same operators (and no ::jsonb casts needed).
        builder.Property(m => m.AttachmentsBeforeJson).HasColumnType("jsonb");
        builder.Property(m => m.AttachmentsAfterJson).HasColumnType("jsonb");
        builder.Property(m => m.EmbedsBeforeJson).HasColumnType("jsonb");
        builder.Property(m => m.EmbedsAfterJson).HasColumnType("jsonb");
    }
}
