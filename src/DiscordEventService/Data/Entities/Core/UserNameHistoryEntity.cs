namespace DiscordEventService.Data.Entities.Core;

public class UserNameHistoryEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? UsernameBefore { get; set; }
    public string? UsernameAfter { get; set; }
    public string? GlobalNameBefore { get; set; }
    public string? GlobalNameAfter { get; set; }
    public DateTime ChangedAtUtc { get; set; }

    public UserEntity User { get; set; } = null!;
}
