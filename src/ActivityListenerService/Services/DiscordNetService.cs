using ActivityListenerService.Models;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using ActivityType = ActivityListenerService.Models.ActivityType;

namespace ActivityListenerService.Services;

public class DiscordNetService : IHostedService
{
    private readonly ILogger<DiscordNetService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsSnapshot<DiscordOptions> _options;
    private readonly DiscordSocketClient _client;
    public DiscordNetService(
        ILogger<DiscordNetService> logger,
        IServiceScopeFactory scopeFactory,
        IOptionsSnapshot<DiscordOptions> options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options;

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.All,
        };
        
        _client = new DiscordSocketClient(config);
        _client.Ready += ReadyAsync;
        _client.PresenceUpdated += PresenceUpdatedAsync;
        _client.MessageReceived += MessageReceivedAsync;
        _client.JoinedGuild += JoinedGuildAsync;
    }

    private async Task JoinedGuildAsync(SocketGuild arg)
    {
        
        var activityType = ActivityType.JoinedGuild;
        _logger.LogInformation("Joined guild [{GuildName}]", arg.Name);
        using var scope = _scopeFactory.CreateScope();
        var publisherService = scope.ServiceProvider.GetRequiredService<IMessagePublisherService>();
        var guild = new DiscordGuild
        {
            DiscordId = arg.Id,
            DiscordTimestamp = arg.CreatedAt.UtcDateTime,
            InviteTimestamp = DateTime.UtcNow,
            Name = arg.Name,
            IconUrl = arg.IconUrl,
            Owner = new DiscordUser
            {
                DiscordId = arg.OwnerId,
                Username = arg.Owner.Username,
                AvatarUrl = arg.Owner.GetAvatarUrl(),
                DiscordTimestamp = arg.Owner.CreatedAt.UtcDateTime,
            },
            Emotes = arg.Emotes.Select(x => new DiscordEmotes
            {
                DiscordId = x.Id,
                Name = x.Name,
                IconUrl = x.Url,
                DiscordTimestamp = x.CreatedAt.UtcDateTime,
            }).ToArray(),
            Members = arg.Users.Select(x => new DiscordUser
            {
                DiscordId = x.Id,
                Username = x.Username,
                AvatarUrl = x.GetAvatarUrl(),
                DiscordTimestamp = x.CreatedAt.UtcDateTime,
            }).ToArray(),
            Channels = arg.Channels.Select(x => new DiscordChannel
            {
                DiscordId = x.Id,
                Name = x.Name,
                DiscordTimestamp = x.CreatedAt.UtcDateTime,
                Messages = Array.Empty<DiscordMessageFull>(),
            }).ToArray(),
        };
        // foreach (var channel in guild.Channels)
        // {
        //     var discordChannel = await _client.GetChannelAsync(channel.DiscordId);
        //     if(discordChannel.GetChannelType() != ChannelType.Text) continue;
        //     var messages = await discordChannel.GetMessagesAsync();
        //
        //     var mappedMessages = messages.Select(async x =>
        //     {
        //         var messageReactions = new List<DiscordReaction>();
        //         foreach (var reaction in x.Reactions.DistinctBy(r => r.Emoji))
        //         {
        //             var users = await x.GetReactionsAsync(reaction.Emoji, 100);
        //             messageReactions.AddRange(users.Select(u => new DiscordReaction
        //             {
        //                 IsRemoved = false,
        //                 MessageId = x.Id,
        //                 UserId = u.Id,
        //                 EmoteId = reaction.Emoji.Id,
        //             }));
        //         }
        //
        //         return new DiscordMessageFull
        //         {
        //             Id = x.Id,
        //             AuthorId = x.Author.Id,
        //             Content = x.Content,
        //             HasAttatchment = x.Attachments.Any(),
        //             IsEdited = x.EditedTimestamp.HasValue,
        //             IsRemoved = false,
        //             DiscordTimestamp = x.CreationTimestamp.UtcDateTime,
        //             ReplyToMessageId = x.ReferencedMessage?.Id,
        //             Reactions = messageReactions,
        //         };
        //     });
        //     
        //     channel.Messages = await Task.WhenAll(mappedMessages);
        // }
        
        var payload = new PublishedMessagePayload<DiscordGuild>
        {
            Payload = guild,
            ActivityType = activityType,
        };
        
        publisherService.PublishActivityAsync(payload);
        
    }

    private async Task MessageReceivedAsync(SocketMessage arg)
    {
        var activityType = ActivityType.MessageCreated;
        _logger.LogInformation("Received [{ActivityType}] from [{AuthorId}] with id [{MessageId}]",
            activityType, arg.Author.Id, arg.Id);
        var message = new DiscordMessage
        {
            Id = arg.Id,
            AuthorId = arg.Author.Id,
            ChannelId = arg.Channel.Id,
            Content = arg.Content,
            DiscordTimestamp = arg.Timestamp.UtcDateTime,
            HasAttatchment = arg.Attachments.Any(),
            ReplyToMessageId = arg.Reference.MessageId.Value,
            IsRemoved = false,
            IsEdited = false,
        };
        
        using var scope = _scopeFactory.CreateScope();
        var publisherService = scope.ServiceProvider.GetRequiredService<IMessagePublisherService>();
        var payload = new PublishedMessagePayload<DiscordMessage>
        {
            Payload = message,
            ActivityType = activityType,
        };

        publisherService.PublishActivityAsync(payload);
        _logger.LogInformation("Published [{ActivityType}] from [{AuthorId}] with id [{MessageId}]",
            activityType, arg.Author.Id, arg.Id);
    }

    private Task PresenceUpdatedAsync(SocketUser arg1, SocketPresence presenceBefore, SocketPresence presenceAfter)
    {
        _logger.LogInformation("Received PresenceUpdated from [{UserId}]", arg1.Id);
        return Task.CompletedTask;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _client.LoginAsync(TokenType.Bot, _options.Value.Token);
        await _client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.StopAsync();
    }
    
    private Task ReadyAsync()
    {
        _logger.LogInformation("Discord.Net is ready");
        return Task.CompletedTask;
    }
}