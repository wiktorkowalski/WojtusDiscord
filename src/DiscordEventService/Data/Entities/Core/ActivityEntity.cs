using System.ComponentModel.DataAnnotations;

namespace DiscordEventService.Data.Entities.Core;

public class ActivityEntity
{
    public Guid Id { get; set; }

    [Required]
    public Guid UserId { get; set; }
    public UserEntity? User { get; set; }

    public ulong UserDiscordId { get; set; }

    public Guid? GuildId { get; set; }
    public GuildEntity? Guild { get; set; }

    public ulong? GuildDiscordId { get; set; }

    // Activity type: Playing, Streaming, Listening, Watching, Custom, Competing
    [Required]
    public int ActivityType { get; set; }

    [MaxLength(256)]
    public string? Name { get; set; }

    [MaxLength(1024)]
    public string? Details { get; set; }

    [MaxLength(1024)]
    public string? State { get; set; }

    // Application info
    public ulong? ApplicationId { get; set; }

    // Timestamps
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }

    // Rich presence images
    [MaxLength(512)]
    public string? LargeImageUrl { get; set; }

    [MaxLength(256)]
    public string? LargeImageText { get; set; }

    [MaxLength(512)]
    public string? SmallImageUrl { get; set; }

    [MaxLength(256)]
    public string? SmallImageText { get; set; }

    // Party info
    [MaxLength(256)]
    public string? PartyId { get; set; }
    public int? PartyCurrentSize { get; set; }
    public int? PartyMaxSize { get; set; }

    // Spotify-specific fields
    [MaxLength(256)]
    public string? SpotifyTrackId { get; set; }

    [MaxLength(512)]
    public string? SpotifyAlbumArtUrl { get; set; }

    [MaxLength(256)]
    public string? SpotifyAlbumTitle { get; set; }

    // JSON array of artist names
    public string? SpotifyArtistsJson { get; set; }

    [MaxLength(256)]
    public string? SpotifySongTitle { get; set; }

    public DateTime? SpotifyTrackStartUtc { get; set; }
    public DateTime? SpotifyTrackEndUtc { get; set; }

    // Streaming-specific fields
    [MaxLength(512)]
    public string? StreamUrl { get; set; }

    // Custom status emoji
    public ulong? CustomStatusEmojiId { get; set; }

    [MaxLength(256)]
    public string? CustomStatusEmojiName { get; set; }

    // Buttons (JSON array)
    public string? ButtonsJson { get; set; }

    // Tracking
    public bool IsActive { get; set; } = true;
    public DateTime FirstSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAtUtc { get; set; }
}
