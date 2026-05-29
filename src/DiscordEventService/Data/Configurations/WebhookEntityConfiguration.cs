using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class WebhookEntityConfiguration : IEntityTypeConfiguration<WebhookEntity>
{
    public void Configure(EntityTypeBuilder<WebhookEntity> builder)
    {
        builder.HasIndex(w => w.DiscordId).IsUnique();
        builder.HasIndex(w => w.GuildId);
        builder.HasIndex(w => w.ChannelId);
        builder.ToTable(t => t.HasCheckConstraint("ck_webhooks_soft_delete", SoftDeleteConstraint.Check));
    }
}
