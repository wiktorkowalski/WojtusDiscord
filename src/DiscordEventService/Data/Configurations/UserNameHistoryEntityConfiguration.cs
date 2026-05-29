using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class UserNameHistoryEntityConfiguration : IEntityTypeConfiguration<UserNameHistoryEntity>
{
    public void Configure(EntityTypeBuilder<UserNameHistoryEntity> builder)
    {
        builder.HasOne(h => h.User)
            .WithMany()
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => h.UserId);
        builder.HasIndex(h => h.ChangedAtUtc);
    }
}
