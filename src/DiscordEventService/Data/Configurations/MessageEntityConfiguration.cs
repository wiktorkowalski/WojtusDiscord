using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class MessageEntityConfiguration : IEntityTypeConfiguration<MessageEntity>
{
    public void Configure(EntityTypeBuilder<MessageEntity> builder)
    {
        builder.HasIndex(m => m.DiscordId).IsUnique();
        builder.HasIndex(m => new { m.GuildId, m.CreatedAtUtc });
        builder.HasIndex(m => m.ChannelId);
        builder.HasIndex(m => m.AuthorId);
        builder.HasIndex(m => new { m.ChannelId, m.IsDeleted });

        builder.Property(m => m.AttachmentsJson).HasColumnType("jsonb");
        builder.Property(m => m.EmbedsJson).HasColumnType("jsonb");

        // §P2.6 / #74: messages.guild_id/channel_id/author_id are NOT NULL FKs,
        // but we never hard-delete guilds/channels/users — they soft-delete with
        // is_deleted+deleted_at_utc. Restrict (vs the EF default of Cascade)
        // protects message history if a delete ever does happen (would surface
        // as a constraint violation rather than silently nuking messages).
        builder.HasOne(m => m.Guild)
            .WithMany()
            .HasForeignKey(m => m.GuildId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Channel)
            .WithMany()
            .HasForeignKey(m => m.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Author)
            .WithMany()
            .HasForeignKey(m => m.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint("ck_messages_soft_delete", SoftDeleteConstraint.Check));
    }
}
