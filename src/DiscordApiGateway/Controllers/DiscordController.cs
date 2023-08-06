using DiscordApiGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace DiscordApiGateway.Controllers;

public class DiscordController : ControllerBase
{
    private readonly ILogger<DiscordController> _logger;
    private readonly DiscordApiService _discordApiService;
    public DiscordController(ILogger<DiscordController> logger, DiscordApiService discordApiService)
    {
        _logger = logger;
        _discordApiService = discordApiService;
    }
    
    [HttpGet("users/{userId}", Name = "GetUserById")]
    public async Task<IActionResult> GetUserById(ulong userId)
    {
        var user = await _discordApiService.GetUserById(userId);
        if (user is null) return NotFound();
        return Ok(user);
    }
    
    [HttpGet("channels/{channelId}/messages/{messageId}", Name = "GetMessageById")]
    public async Task<IActionResult> GetMessageById(ulong channelId, ulong messageId)
    {
        var message = await _discordApiService.GetMessageById(channelId, messageId);
        if (message == null) return NotFound();
        return Ok(message);
    }
    
    [HttpGet("channels/{channelId}", Name = "GetChannelById")]
    public async Task<IActionResult> GetChannelById(ulong channelId)
    {
        var channel = await _discordApiService.GetChannelById(channelId);
        if (channel == null) return NotFound();
        return Ok(channel);
    }
    
    [HttpGet("guilds/{guildId}", Name = "GetGuildById")]
    public async Task<IActionResult> GetGuildById(ulong guildId)
    {
        var guild = await _discordApiService.GetGuildById(guildId);
        if (guild == null) return NotFound();
        return Ok(guild);
    }
    
    [HttpGet("guilds/{guildId}/members/{memberId}", Name = "GetMemberById")]
    public async Task<IActionResult> GetMemberById(ulong guildId, ulong memberId)
    {
        var member = await _discordApiService.GetMemberById(guildId, memberId);
        if (member == null) return NotFound();
        return Ok(member);
    }
    
    [HttpGet("guilds/{guildId}/emojis/{emojiId}", Name = "GetEmojiById")]
    public async Task<IActionResult> GetEmojiById(ulong guildId, ulong emojiId)
    {
        var emoji = await _discordApiService.GetEmoteById(guildId, emojiId);
        if (emoji == null) return NotFound();
        return Ok(emoji);
    }
}