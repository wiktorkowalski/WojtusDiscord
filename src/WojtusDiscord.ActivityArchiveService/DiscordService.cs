using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Mappers;
using WojtusDiscord.ActivityArchiveService.Models;
using WojtusDiscord.ActivityArchiveService.Services;

namespace WojtusDiscord.ActivityArchiveService
{
    public class DiscordService : IHostedService
    {
        private const int MAX_SCRAPE_MESSAGES = 100;

        private readonly ILogger<DiscordService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DiscordClient _discordClient;

        public DiscordService(ILogger<DiscordService> logger, IConfiguration configuration, IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _discordClient = new DiscordClient(new DiscordConfiguration
            {
                Token = configuration.GetValue<string>("DiscordToken"),
                Intents = DiscordIntents.All,
                LoggerFactory = loggerFactory,

            });
            //TODO: properly use context
            var temp = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ActivityArchiveContext>();
            temp.Database.IsNpgsql();

            //TODO: move to separate service
            _discordClient.MessageCreated += MessageCreated;
            _discordClient.MessageUpdated += MessageUpdated;
            _discordClient.MessageDeleted += MessageDeleted;
            //_discordClient.MessagesBulkDeleted += MessageBuldDeleted;

            _discordClient.MessageReactionAdded += ReactionAdded;
            _discordClient.MessageReactionRemoved += ReactionRemoved;
            //_discordClient.MessageReactionRemovedEmoji += ReactionsRemovedForEmote;
            //_discordClient.MessageReactionsCleared += ReactionsCleared;

            _discordClient.TypingStarted += TypingStarted;
            _discordClient.VoiceStateUpdated += VoiceStateUpdated;

            _discordClient.PresenceUpdated += PresenceUpdated;

            _discordClient.GuildCreated += JoinedGuild;
            //_discordClient.GuildMemberAdded += GuildMemberAdded;
            //_discordClient.GuildMemberUpdated += GuildMemberUpdated;
            // and so on

            _logger.LogInformation($"Done initializing {nameof(DiscordService)}");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting DiscordService...");
            await _discordClient.ConnectAsync();
            _logger.LogInformation("Started DiscordService");
            await Task.Delay(1000).ContinueWith(async t =>
            {
                await _discordClient.UpdateStatusAsync(userStatus: DSharpPlus.Entities.UserStatus.DoNotDisturb);
            });
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping DiscordService...");
            await _discordClient.DisconnectAsync();
            _logger.LogInformation("Stopped DiscordService");
        }

        private async Task JoinedGuild(DiscordClient sender, GuildCreateEventArgs guildCreateEventArgs)
        {
            _logger.LogInformation($"[Guild][Join][{guildCreateEventArgs.Guild.Name}]");
            using var scope = _scopeFactory.CreateScope();
            var guildInitService = scope.ServiceProvider.GetRequiredService<GuildInitializerService>();

            var webhooks = guildCreateEventArgs.Guild.GetWebhooksAsync().Result.Select(w => DiscordMapper.MapWebhook(w)).ToArray();
            var users = guildCreateEventArgs.Guild.Members.Select(u => DiscordMapper.MapUser(u.Value)).Concat(webhooks).ToArray();
            var guild = DiscordMapper.MapGuild(guildCreateEventArgs.Guild, users.First(u => u.DiscordId == guildCreateEventArgs.Guild.OwnerId));

            users = await guildInitService.CreateUsers(users);
            guild = await guildInitService.CreateGuild(guild);
            await guildInitService.CreateGuildMembers(guild, users);

            var emotes = guildCreateEventArgs.Guild.Emojis
                .Select(e => DiscordMapper.MapEmote(e.Value))
                .ToArray();
            var textChannels = guildCreateEventArgs.Guild.Channels
                .Where(c => c.Value.Type == ChannelType.Text)
                .Select(c => DiscordMapper.MapTextChannel(c.Value, guild))
                .ToArray();
            var voiceChannels = guildCreateEventArgs.Guild.Channels
                .Where(c => c.Value.Type == ChannelType.Voice)
                .Select(c => DiscordMapper.MapVoiceChannel(c.Value, guild))
                .ToArray();

            emotes = await guildInitService.CreateEmotes(emotes);
            textChannels = await guildInitService.CreateTextChannels(textChannels);
            voiceChannels = await guildInitService.CreateVoiceChannels(voiceChannels);

            foreach (var channel in textChannels)
            {
                var messagesToSave = new List<DiscordMessage>();

                var discordChannel = guildCreateEventArgs.Guild.Channels.First(c => c.Value.Id == channel.DiscordId).Value;
                var discordMessages = (await discordChannel.GetMessagesAsync(MAX_SCRAPE_MESSAGES)).ToArray();
                _logger.LogInformation($"Got {discordMessages.Length} messages from {channel.Name}");
                foreach (var discordMessage in discordMessages)
                {
                    var author = users.FirstOrDefault(u => u.DiscordId == discordMessage.Author.Id);
                    if (author is null)
                    {
                        author = await guildInitService.CreateUser(DiscordMapper.MapUser(discordMessage.Author));
                    }

                    var message = DiscordMapper.MapMessage(discordMessage, author, channel);

                    if (discordMessage.Reactions.Any())
                    {
                        var reactionsToSave = new List<DiscordReaction>();
                        foreach (var reaction in discordMessage.Reactions.DistinctBy(r => r.Emoji))
                        {
                            var usersReacted = (await discordMessage.GetReactionsAsync(reaction.Emoji)).Select(u => DiscordMapper.MapUser(u)).ToArray();
                            var emote = emotes.FirstOrDefault(e => e.DiscordId == reaction.Emoji.Id);
                            if (emote is null) emote = await guildInitService.CreateEmote(DiscordMapper.MapEmote(reaction.Emoji));
                            foreach (var userReacted in usersReacted)
                            {
                                var user = await guildInitService.CreateUser(userReacted);
                                reactionsToSave.Add(DiscordMapper.MapReaction(message, user, emote));
                            }
                        }
                        message.Reactions = reactionsToSave;
                    }
                    messagesToSave.Add(message);
                }

                await guildInitService.CreateMessages(messagesToSave.ToArray());
            }

            _logger.LogInformation("Done initializing guild");
        }

        private async Task MessageCreated(DiscordClient sender, MessageCreateEventArgs messageCreateEventArgs)
        {
            _logger.LogInformation($"[Message][{messageCreateEventArgs.Message.Id}][{messageCreateEventArgs.Author.Username}]");
            using var scope = _scopeFactory.CreateScope();

            var messageService = scope.ServiceProvider.GetRequiredService<DiscordMessageService>();
            var channelService = scope.ServiceProvider.GetRequiredService<DiscordTextChannelService>();
            var userService = scope.ServiceProvider.GetRequiredService<DiscordUserService>();
            var guildService = scope.ServiceProvider.GetRequiredService<DiscordGuildService>();

            var author = userService.GetByDiscordId(messageCreateEventArgs.Author.Id);
            if (author is null)
            {
                author = userService.Create(DiscordMapper.MapUser(messageCreateEventArgs.Author));
            }
            var channel = channelService.GetByDiscordId(messageCreateEventArgs.Channel.Id);
            if (channel is null)
            {
                var guild = guildService.GetByDiscordId(messageCreateEventArgs.Guild.Id);
                channel = channelService.Create(DiscordMapper.MapTextChannel(messageCreateEventArgs.Channel, guild));
            }

            var message = DiscordMapper.MapMessage(messageCreateEventArgs.Message, author, channel);
            if (messageCreateEventArgs.Message.ReferencedMessage is not null)
            {
                var referencedMessage = messageService.GetByDiscordId(messageCreateEventArgs.Message.ReferencedMessage.Id);
                if (referencedMessage is not null)
                {
                    message.ReplyToMessage = referencedMessage;
                }
            }
            
            messageService.Create(message);
        }

        private async Task MessageUpdated(DiscordClient sender, MessageUpdateEventArgs messageUpdateEventArgs)
        {
            _logger.LogInformation($"[Message][Update][{messageUpdateEventArgs.Message.Id}][{messageUpdateEventArgs.Author.Username}]");
            using var scope = _scopeFactory.CreateScope();

            var messageService = scope.ServiceProvider.GetRequiredService<DiscordMessageService>();
            var channelService = scope.ServiceProvider.GetRequiredService<DiscordTextChannelService>();
            var userService = scope.ServiceProvider.GetRequiredService<DiscordUserService>();
            var guildService = scope.ServiceProvider.GetRequiredService<DiscordGuildService>();

            var author = userService.GetByDiscordId(messageUpdateEventArgs.Author.Id);
            if (author is null)
            {
                author = userService.Create(DiscordMapper.MapUser(messageUpdateEventArgs.Author));
            }
            var channel = channelService.GetByDiscordId(messageUpdateEventArgs.Channel.Id);
            if (channel is null)
            {
                var guild = guildService.GetByDiscordId(messageUpdateEventArgs.Guild.Id);
                channel = channelService.Create(DiscordMapper.MapTextChannel(messageUpdateEventArgs.Channel, guild));
            }
            var message = messageService.GetByDiscordId(messageUpdateEventArgs.Message.Id);
            if(message is null)
            {
                message = messageService.Create(DiscordMapper.MapMessage(messageUpdateEventArgs.Message, author, channel));
            }

            message.IsEdited = true;

            messageService.CreateContentEdit(new DiscordMessageContentEdit
            {
                Message = message,
                IsRemoved = false,
                Content = messageUpdateEventArgs.Message.Content,
                ContentBefore = messageUpdateEventArgs.MessageBefore.Content,
            });
        }

        private async Task MessageDeleted(DiscordClient sender, MessageDeleteEventArgs messageDeleteEventArgs)
        {
            _logger.LogInformation($"[Message][Delete][{messageDeleteEventArgs.Message.Id}][{messageDeleteEventArgs.Message.Author.Username}]");
            using var scope = _scopeFactory.CreateScope();

            var messageService = scope.ServiceProvider.GetRequiredService<DiscordMessageService>();
            var message = messageService.GetByDiscordId(messageDeleteEventArgs.Message.Id);
            if (message is null) return;

            message.IsRemoved = true;
            messageService.CreateContentEdit(new DiscordMessageContentEdit
            {
                Message = message,
                IsRemoved = true,
                Content = null,
                ContentBefore = messageDeleteEventArgs.Message.Content,
            });
        }

        private async Task ReactionAdded(DiscordClient sender, MessageReactionAddEventArgs messageReactionAddEventArgs)
        {
            _logger.LogInformation($"[Reaction][Add][{messageReactionAddEventArgs.Message.Id}][{messageReactionAddEventArgs.Emoji.Name}]");
            using var scope = _scopeFactory.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<DiscordUserService>();
            var channelService = scope.ServiceProvider.GetRequiredService<DiscordTextChannelService>();
            var messageService = scope.ServiceProvider.GetRequiredService<DiscordMessageService>();
            var reactionService = scope.ServiceProvider.GetRequiredService<DiscordReactionService>();
            var emoteService = scope.ServiceProvider.GetRequiredService<DiscordEmoteService>();

            var message = messageService.GetByDiscordId(messageReactionAddEventArgs.Message.Id);
            if (message is null)
            {
                var author = userService.GetByDiscordId(messageReactionAddEventArgs.Message.Author.Id);
                var channel = channelService.GetByDiscordId(messageReactionAddEventArgs.Message.ChannelId);
                message = messageService.Create(DiscordMapper.MapMessage(messageReactionAddEventArgs.Message, author, channel));
            }
            var emote = emoteService.GetByDiscordId(messageReactionAddEventArgs.Emoji.Id);
            if(emote is null)
            {
                emote = emoteService.Create(DiscordMapper.MapEmote(messageReactionAddEventArgs.Emoji));
            }
            var user = userService.GetByDiscordId(messageReactionAddEventArgs.User.Id);
            if(user is null)
            {
                user = userService.Create(DiscordMapper.MapUser(messageReactionAddEventArgs.User));
            }

            reactionService.Create(DiscordMapper.MapReaction(message, user, emote));
        }

        private async Task ReactionRemoved(DiscordClient sender, MessageReactionRemoveEventArgs messageReactionRemoveEventArgs)
        {
            _logger.LogInformation($"[Reaction][Remove][{messageReactionRemoveEventArgs.Message.Id}][{messageReactionRemoveEventArgs.Emoji.Name}]");
            using var scope = _scopeFactory.CreateScope();
            var reactionService = scope.ServiceProvider.GetRequiredService<DiscordReactionService>();

            var reaction = messageReactionRemoveEventArgs;
            reactionService.SetAsRemoved(reaction.User.Id, reaction.Message.Id, reaction.Emoji.Id);
        }

        private async Task TypingStarted(DiscordClient sender, TypingStartEventArgs typingStartEventArgs)
        {
            _logger.LogInformation($"[Typing][{typingStartEventArgs.Channel.Id}][{typingStartEventArgs.User.Username}]");
            using var scope = _scopeFactory.CreateScope();
            var channelService = scope.ServiceProvider.GetRequiredService<DiscordTextChannelService>();
            var userService = scope.ServiceProvider.GetRequiredService<DiscordUserService>();

            var channel = channelService.GetByDiscordId(typingStartEventArgs.Channel.Id);
            var user = userService.GetByDiscordId(typingStartEventArgs.User.Id);

            channelService.CreateTypingStatus(new DiscordTypingStatus
            {
                TextChannel = channel,
                User = user,
            });
        }

        private async Task VoiceStateUpdated(DiscordClient sender, VoiceStateUpdateEventArgs voiceStateUpdateEventArgs)
        {
            _logger.LogInformation($"[Voice][{voiceStateUpdateEventArgs.Channel.Name}][{voiceStateUpdateEventArgs.User.Username}]");
            using var scope = _scopeFactory.CreateScope();
            var channelService = scope.ServiceProvider.GetRequiredService<DiscordVoiceChannelService>();
            var userService = scope.ServiceProvider.GetRequiredService<DiscordUserService>();

            var channel = channelService.GetByDiscordId(voiceStateUpdateEventArgs.Channel.Id);
            var user = userService.GetByDiscordId(voiceStateUpdateEventArgs.User.Id);

            channelService.CreateVoiceState(DiscordMapper.MapVoiceStatus(voiceStateUpdateEventArgs.Before, voiceStateUpdateEventArgs.After, channel, user));
        }

        private async Task PresenceUpdated(DiscordClient sender, PresenceUpdateEventArgs presenceUpdateEventArgs)
        {
            _logger.LogInformation($"[Presence][{presenceUpdateEventArgs.User.Username}]");
            var temp = JsonConvert.SerializeObject(presenceUpdateEventArgs, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            });
            using var scope = _scopeFactory.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<DiscordUserService>();
            var user = userService.GetByDiscordId(presenceUpdateEventArgs.User.Id);

            userService.CreatePresenceStatus(DiscordMapper.MapPresenceStatus(presenceUpdateEventArgs.PresenceBefore, presenceUpdateEventArgs.PresenceAfter, user));
        }
    }
}
