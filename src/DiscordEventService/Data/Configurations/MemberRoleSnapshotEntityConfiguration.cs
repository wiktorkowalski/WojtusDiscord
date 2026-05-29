using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class MemberRoleSnapshotEntityConfiguration : IEntityTypeConfiguration<MemberRoleSnapshotEntity>
{
    public void Configure(EntityTypeBuilder<MemberRoleSnapshotEntity> builder)
    {
        builder.HasOne(s => s.Member)
            .WithMany()
            .HasForeignKey(s => s.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.MemberId, s.RoleDiscordId, s.GrantedAtUtc });

        builder.HasIndex(s => new { s.MemberId, s.RoleDiscordId })
            .IsUnique()
            .HasFilter("revoked_at_utc IS NULL");
    }
}
