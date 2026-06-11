using System.ComponentModel.DataAnnotations;

namespace DiscordEventService.Configuration;

public class DiscordOptions
{
    public const string SectionName = "Discord";

    [Required]
    [MinLength(50, ErrorMessage = "Discord token appears invalid (too short)")]
    public string Token { get; set; } = string.Empty;

    // #224: guild to register slash commands on (guild-scoped registration is
    // instant; global takes up to an hour to propagate). Unset → the commands
    // subsystem is not wired at all and the bot stays a pure passive logger.
    public ulong? CommandGuildId { get; set; }
}
