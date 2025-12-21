using System.ComponentModel.DataAnnotations;

namespace DiscordEventService.Configuration;

public class DiscordOptions
{
    public const string SectionName = "Discord";

    [Required]
    [MinLength(50, ErrorMessage = "Discord token appears invalid (too short)")]
    public string Token { get; set; } = string.Empty;
}
