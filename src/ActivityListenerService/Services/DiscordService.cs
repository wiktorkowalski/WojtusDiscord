﻿using ActivityListenerService.Mappers;
using ActivityListenerService.Models;
using ActivityListenerService.Services;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace ActivityListenerService;

public class DiscordService : IHostedService
{
    private readonly ILogger<DiscordService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordClient _discordClient;

    public DiscordService(ILogger<DiscordService> _logger, IServiceScopeFactory _scopeFactory)
    {
        this._logger = _logger;
        this._scopeFactory = _scopeFactory;
        _discordClient = new DiscordClient(new DiscordConfiguration
        {
            Token = Environment.GetEnvironmentVariable("DiscordToken"),
            TokenType = TokenType.Bot,
            Intents = DiscordIntents.All,
        });

        //TODO: move to separate service
        _discordClient.MessageCreated += MessageCreated;
        _discordClient.MessageUpdated += MessageUpdated;
        _discordClient.MessageDeleted += MessageDeleted;
        //_discordClient.MessagesBulkDeleted += MessageBuldDeleted;

        _discordClient.MessageReactionAdded += ReactionAdded;
        _discordClient.MessageReactionRemoved += ReactionRemoved;
        // _discordClient.MessageReactionRemovedEmoji += ReactionsRemovedForEmote;
        // _discordClient.MessageReactionsCleared += ReactionsCleared;

        _discordClient.TypingStarted += TypingStarted;
        _discordClient.VoiceStateUpdated += VoiceStateUpdated;

        _discordClient.PresenceUpdated += PresenceUpdated;

        // _discordClient.GuildCreated += JoinedGuild;
        //_discordClient.GuildMemberAdded += GuildMemberAdded;
        //_discordClient.GuildMemberUpdated += GuildMemberUpdated;
        //_discordClient.ChannelCreated += ChannelCreated;
        //_discordClient.ChannelUpdated += ChannelUpdated;
        // and so on
        _logger.LogInformation($"Done initializing {nameof(DiscordService)}");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connecting to Discord...");
        await _discordClient.ConnectAsync(status: DSharpPlus.Entities.UserStatus.DoNotDisturb);
        _logger.LogInformation("Connected to Discord");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Disconnecting from Discord...");
        await _discordClient.DisconnectAsync();
        _logger.LogInformation("Disconnected from Discord");
    }

    private async Task MessageCreated(DiscordClient sender, MessageCreateEventArgs messageCreateEventArgs)
    {
        var activityType = ActivityType.MessageCreated;
        _logger.LogInformation("Received [{ActivityType}][{MessageId}][{AuthorUsername}]",
            activityType, messageCreateEventArgs.Message.Id, messageCreateEventArgs.Author.Username);
        var message = messageCreateEventArgs.MapToDiscordMessage();
        using var scope = _scopeFactory.CreateScope();
        var publisherService = scope.ServiceProvider.GetRequiredService<IMessagePublisherService>();
        var payload = new PublishedMessagePayload<DiscordMessage>
        {
            Payload = message,
            ActivityType = activityType,
        };

        await publisherService.PublishActivityAsync(payload);
        _logger.LogInformation("Published [{ActivityType}][{MessageId}]", activityType,
            messageCreateEventArgs.Message.Id);
    }

    private async Task MessageUpdated(DiscordClient sender, MessageUpdateEventArgs messageUpdateEventArgs)
    {
        var activityType = ActivityType.MessageUpdated;
        _logger.LogInformation("Received [{ActivityType}][{MessageId}][{AuthorUsername}]",
            activityType, messageUpdateEventArgs.Message.Id, messageUpdateEventArgs.Author.Username);
        var messageEdit = messageUpdateEventArgs.MapToDiscordMessageEdit();
        using var scope = _scopeFactory.CreateScope();
        var publisherService = scope.ServiceProvider.GetRequiredService<IMessagePublisherService>();
        var payload = new PublishedMessagePayload<DiscordMessageEdit>
        {
            Payload = messageEdit,
            ActivityType = activityType,
        };

        await publisherService.PublishActivityAsync(payload);
        _logger.LogInformation("Published [{ActivityType}][{MessageId}]", activityType,
            messageUpdateEventArgs.Message.Id);
    }

    private async Task MessageDeleted(DiscordClient sender, MessageDeleteEventArgs messageDeleteEventArgs)
    {
        var activityType = ActivityType.MessageDeleted;
        _logger.LogInformation("Received [{ActivityType}][{MessageId}]",
            activityType, messageDeleteEventArgs.Message.Id);
        var messageDeleted = messageDeleteEventArgs.MapToDiscordMessageEdit();
        using var scope = _scopeFactory.CreateScope();
        var publisherService = scope.ServiceProvider.GetRequiredService<IMessagePublisherService>();
        var payload = new PublishedMessagePayload<DiscordMessageEdit>
        {
            Payload = messageDeleted,
            ActivityType = activityType,
        };

        await publisherService.PublishActivityAsync(payload);
        _logger.LogInformation("Published [{ActivityType}][{MessageId}]", activityType,
            messageDeleteEventArgs.Message.Id);
    }

    private async Task ReactionAdded(DiscordClient sender, MessageReactionAddEventArgs messageReactionAddEventArgs)
    {
        var activityType = ActivityType.ReactionAdded;
        _logger.LogInformation("Received [{ActivityType}][{MessageId}][{EmojiName}]",
            activityType, messageReactionAddEventArgs.Message.Id, messageReactionAddEventArgs.Emoji.Name);
        var reaction = messageReactionAddEventArgs.MapToDiscordReaction();
        using var scope = _scopeFactory.CreateScope();
        var publisherService = scope.ServiceProvider.GetRequiredService<IMessagePublisherService>();
        
        var payload = new PublishedMessagePayload<DiscordReaction>
        {
            Payload = reaction,
            ActivityType = activityType,
        };
        
        await publisherService.PublishActivityAsync(payload);
        _logger.LogInformation("Published [{ActivityType}][{MessageId}]", activityType,
            messageReactionAddEventArgs.Message.Id);
    }

    private async Task ReactionRemoved(DiscordClient sender,
        MessageReactionRemoveEventArgs messageReactionRemoveEventArgs)
    {
        var activityType = ActivityType.ReactionRemoved;
        _logger.LogInformation("[Reaction][Remove][{MessageId}][{EmojiName}]", messageReactionRemoveEventArgs.Message.Id, messageReactionRemoveEventArgs.Emoji.Name);
        var reaction = messageReactionRemoveEventArgs.MapToDiscordReaction();
        using var scope = _scopeFactory.CreateScope();
        var publisherService = scope.ServiceProvider.GetRequiredService<IMessagePublisherService>();
        
        var payload = new PublishedMessagePayload<DiscordReaction>
        {
            Payload = reaction,
            ActivityType = activityType,
        };
        
        await publisherService.PublishActivityAsync(payload);
        _logger.LogInformation("Published [{ActivityType}][{MessageId}]", activityType,
            messageReactionRemoveEventArgs.Message.Id);
    }
    
    private async Task TypingStarted(DiscordClient sender, TypingStartEventArgs typingStartEventArgs)
    {
        var activityType = ActivityType.TypingStarted;
        _logger.LogInformation("[Typing][Started][{ChannelId}][{UserId}]", typingStartEventArgs.Channel.Id, typingStartEventArgs.User.Id);
        var typing = typingStartEventArgs.MapToDiscordTyping();
        using var scope = _scopeFactory.CreateScope();
        var publisherService = scope.ServiceProvider.GetRequiredService<IMessagePublisherService>();
        
        var payload = new PublishedMessagePayload<DiscordTyping>
        {
            Payload = typing,
            ActivityType = activityType,
        };
        
        await publisherService.PublishActivityAsync(payload);
        _logger.LogInformation("Published [{ActivityType}][{ChannelId}][{UserId}]", activityType,
            typingStartEventArgs.Channel.Id, typingStartEventArgs.User.Id);
    }
    
    private async Task VoiceStateUpdated(DiscordClient sender, VoiceStateUpdateEventArgs voiceStateUpdateEventArgs)
    {
        var activityType = ActivityType.VoiceStateUpdated;
        _logger.LogInformation("[Voice][State][Updated][{ChannelId}][{UserId}]", voiceStateUpdateEventArgs.Channel.Id, voiceStateUpdateEventArgs.User.Id);
        var voiceState = voiceStateUpdateEventArgs.MapToDiscordVoiceState();
        using var scope = _scopeFactory.CreateScope();
        var publisherService = scope.ServiceProvider.GetRequiredService<IMessagePublisherService>();
        
        var payload = new PublishedMessagePayload<DiscordVoiceState>
        {
            Payload = voiceState,
            ActivityType = activityType,
        };
        
        await publisherService.PublishActivityAsync(payload);
        _logger.LogInformation("Published [{ActivityType}][{ChannelId}]", activityType,
            voiceStateUpdateEventArgs.Channel.Id);
    }
    
    private async Task PresenceUpdated(DiscordClient sender, PresenceUpdateEventArgs presenceUpdateEventArgs)
    {
        var activityType = ActivityType.PresenceUpdated;
        _logger.LogInformation("[Presence][Updated][{UserId}]", presenceUpdateEventArgs.User.Id);
        var presence = presenceUpdateEventArgs.MapToDiscordPresenceStatus();
        using var scope = _scopeFactory.CreateScope();
        var publisherService = scope.ServiceProvider.GetRequiredService<IMessagePublisherService>();
        
        var payload = new PublishedMessagePayload<DiscordPresenceStatus>
        {
            Payload = presence,
            ActivityType = activityType,
        };
        
        await publisherService.PublishActivityAsync(payload);
        _logger.LogInformation("Published [{ActivityType}][{UserId}]", activityType,
            presenceUpdateEventArgs.User.Id);
    }
}