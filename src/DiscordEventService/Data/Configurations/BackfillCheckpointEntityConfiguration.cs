using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class BackfillCheckpointEntityConfiguration : IEntityTypeConfiguration<BackfillCheckpointEntity>
{
    public void Configure(EntityTypeBuilder<BackfillCheckpointEntity> builder)
    {
        builder.HasIndex(b => new { b.GuildDiscordId, b.Type }).IsUnique();
        builder.HasIndex(b => b.Status);
        builder.HasIndex(b => b.HangfireJobId);
    }
}
