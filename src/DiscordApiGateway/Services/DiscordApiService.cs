using DiscordApiGateway.Mappers;
using DiscordApiGateway.Models;
using DSharpPlus;

namespace DiscordApiGateway.Services;

public class DiscordApiService
{
    private readonly ILogger<DiscordApiService> _logger;
    private readonly DiscordClient _discordClient;

    public DiscordApiService(ILogger<DiscordApiService> logger)
    {
        _logger = logger;
        _discordClient = new DiscordClient( new DiscordConfiguration
        {
            Token = Environment.GetEnvironmentVariable("DiscordToken"),
            TokenType = TokenType.Bot,
            Intents = DiscordIntents.All
        });
        
        _logger.LogInformation("Connecting to Discord...");
        _discordClient.ConnectAsync().Wait();
        _logger.LogInformation("Connected to Discord");
    }

    public async Task<DiscordUser> GetUserById(ulong userId)
    {
        var user = await _discordClient.GetUserAsync(userId);
        return user.MapUser();
    }
    
    public async Task<DiscordMessage> GetMessageById(ulong channelId, ulong messageId)
    {
        var channel = await _discordClient.GetChannelAsync(channelId);
        if (channel == null) return null;
        var message = await channel.GetMessageAsync(messageId);
        return message.MapMessage();
    }
    
    public async Task<DiscordChannel> GetChannelById(ulong channelId)
    {
        var channel = await _discordClient.GetChannelAsync(channelId);
        return channel.MapChannel();
    }
    
    public async Task<DiscordGuild> GetGuildById(ulong guildId)
    {
        var guild = await _discordClient.GetGuildAsync(guildId);
        return guild.MapGuild();
    }
    
    public async Task<DiscordMember> GetMemberById(ulong guildId, ulong memberId)
    {
        var guild = await _discordClient.GetGuildAsync(guildId);
        if (guild == null) return null;
        var memeber = await guild.GetMemberAsync(memberId);
        return memeber.MapMember();
    }
    
    public async Task<DiscordEmote> GetEmoteById(ulong guildId, ulong emojiId)
    {
        var guild = await _discordClient.GetGuildAsync(guildId);
        if (guild == null) return null;
        var emote = await guild.GetEmojiAsync(emojiId);
        return emote.MapEmote();
    }
}