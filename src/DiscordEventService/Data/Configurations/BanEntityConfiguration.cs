using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class BanEntityConfiguration : IEntityTypeConfiguration<BanEntity>
{
    public void Configure(EntityTypeBuilder<BanEntity> builder)
    {
        builder.HasIndex(b => b.GuildId);
        builder.HasIndex(b => new { b.GuildId, b.UserId });
        builder.HasIndex(b => new { b.GuildId, b.IsActive });
    }
}
