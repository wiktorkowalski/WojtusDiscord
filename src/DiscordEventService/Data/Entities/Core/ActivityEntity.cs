namespace DiscordEventService.Data.Entities.Core;

public class ActivityEntity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;

    public Guid? GuildId { get; set; }
    public GuildEntity? Guild { get; set; }

    // Activity type: Playing, Streaming, Listening, Watching, Custom, Competing
    public int ActivityType { get; set; }

    public string? Name { get; set; }

    public string? Details { get; set; }

    public string? State { get; set; }

    public ulong? ApplicationId { get; set; }

    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }

    public string? LargeImageUrl { get; set; }

    public string? LargeImageText { get; set; }

    public string? SmallImageUrl { get; set; }

    public string? SmallImageText { get; set; }

    public string? PartyId { get; set; }
    public int? PartyCurrentSize { get; set; }
    public int? PartyMaxSize { get; set; }

    public string? SpotifyTrackId { get; set; }

    public string? SpotifyAlbumArtUrl { get; set; }

    public string? SpotifyAlbumTitle { get; set; }

    // JSON array of artist names
    public string? SpotifyArtistsJson { get; set; }

    public string? SpotifySongTitle { get; set; }

    public DateTime? SpotifyTrackStartUtc { get; set; }
    public DateTime? SpotifyTrackEndUtc { get; set; }

    public string? StreamUrl { get; set; }

    public ulong? CustomStatusEmojiId { get; set; }

    public string? CustomStatusEmojiName { get; set; }

    // Buttons (JSON array)
    public string? ButtonsJson { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime FirstSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAtUtc { get; set; }
}
