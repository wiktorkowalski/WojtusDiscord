using DiscordApiGateway.Mappers;
using DiscordApiGateway.Models;
using DiscordApiGateway.Options;
using DSharpPlus;
using Microsoft.Extensions.Options;

namespace DiscordApiGateway.Services;

public class DiscordApiService : IHostedService
{
    private readonly ILogger<DiscordApiService> _logger;
    private readonly IOptions<DiscordOptions> _options;
    private readonly DiscordClient _discordClient;

    public DiscordApiService(ILogger<DiscordApiService> logger, IOptions<DiscordOptions> options)
    {
        _logger = logger;
        _options = options;
        _discordClient = new DiscordClient( new DiscordConfiguration
        {
            Token = _options.Value.Token,
            TokenType = TokenType.Bot,
            Intents = DiscordIntents.All
        });
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connecting to Discord...");
        await _discordClient.ConnectAsync();
        _logger.LogInformation("Connected to Discord");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Disconnecting from Discord...");
        await _discordClient.DisconnectAsync();
        _logger.LogInformation("Disconnected from Discord");
    }
}