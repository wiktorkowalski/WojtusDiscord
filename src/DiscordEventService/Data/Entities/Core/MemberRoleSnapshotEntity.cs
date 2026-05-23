namespace DiscordEventService.Data.Entities.Core;

public class MemberRoleSnapshotEntity
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public ulong RoleDiscordId { get; set; }
    public DateTime GrantedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? SourceEventId { get; set; }

    // Navigation properties
    public MemberEntity Member { get; set; } = null!;
}
