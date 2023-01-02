using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
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
            _logger.LogInformation($"Done initializing {nameof(DiscordService)}");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting DiscordService...");
            await _discordClient.ConnectAsync();
            _logger.LogInformation("Started DiscordService");
            await Task.Delay(1000).ContinueWith(async t =>
            {
                await _discordClient.UpdateStatusAsync(userStatus: DSharpPlus.Entities.UserStatus.Invisible);
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
                                reactionsToSave.Add(DiscordMapper.MapReaction(reaction, message, user, emote));
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
            var channelService = scope.ServiceProvider.GetRequiredService<DiscordChannelService>();
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
            _logger.LogInformation($"[Message][{messageCreateEventArgs.Message.Id}][{messageCreateEventArgs.Author.Username}][Saved]");
        }

        private async Task MessageUpdated(DiscordClient sender, MessageUpdateEventArgs messageUpdateEventArgs)
        {
            _logger.LogInformation($"[Message][Update][{messageUpdateEventArgs.Message.Id}][{messageUpdateEventArgs.Author.Username}]");
            using var scope = _scopeFactory.CreateScope();

            var messageService = scope.ServiceProvider.GetRequiredService<DiscordMessageService>();
            var channelService = scope.ServiceProvider.GetRequiredService<DiscordChannelService>();
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

        //private async Task MessageBuldDeleted(DiscordClient sender, MessageBulkDeleteEventArgs messageBulkDeleteEventArgs)
        //{
        //    _logger.LogInformation("[Message][BulkDelete]");
        //    var messages = await _dbContext.DiscordMessages.Where(message => messageBulkDeleteEventArgs.Messages.Any(m => m.Id == message.DiscordId)).ToListAsync();
        //    foreach (var message in messages)
        //    {
        //        message.IsRemoved = true;
        //        await _dbContext.DiscordMessageContentEdit.AddAsync(new DiscordMessageContentEdit
        //        {
        //            Message = message,
        //            IsRemoved = true,
        //            Content = null,
        //            ContentBefore = message.Content
        //        });
        //    }
        //    await _dbContext.SaveChangesAsync();
        //}

        private async Task ReactionAdded(DiscordClient sender, MessageReactionAddEventArgs messageReactionAddEventArgs)
        {
            _logger.LogInformation($"[Reaction][Add][{messageReactionAddEventArgs.Message.Id}][{messageReactionAddEventArgs.Emoji.Name}]");
            var message = await _dbContext.DiscordMessages.SingleOrDefaultAsync(message => message.DiscordId == messageReactionAddEventArgs.Message.Id) ?? new DiscordMessage
            {
                DiscordId = messageReactionAddEventArgs.Message.Id,
                Author = _dbContext.DiscordUsers.First(user => user.DiscordId == messageReactionAddEventArgs.Message.Author.Id),
                Content = messageReactionAddEventArgs.Message.Content,
                HasAttatchment = messageReactionAddEventArgs.Message.Attachments.Any(),
            };
            //TODO: move creating to separate service
            var user = await _dbContext.DiscordUsers.SingleOrDefaultAsync(user => user.DiscordId == messageReactionAddEventArgs.User.Id) ?? new DiscordUser
            {
                DiscordId = messageReactionAddEventArgs.User.Id,
                Username = messageReactionAddEventArgs.User.Username,
                Discriminator = messageReactionAddEventArgs.User.Discriminator,
                AvatarUrl = messageReactionAddEventArgs.User.AvatarUrl,
                IsBot = messageReactionAddEventArgs.User.IsBot,
            };
            var emote = await _dbContext.DiscordEmotes.SingleOrDefaultAsync(emote => emote.DiscordId == messageReactionAddEventArgs.Emoji.Id) ?? new DiscordEmote
            {
                DiscordId = messageReactionAddEventArgs.Emoji.Id,
                Name = messageReactionAddEventArgs.Emoji.Name,
                IsAnimated = messageReactionAddEventArgs.Emoji.IsAnimated,
                Url = messageReactionAddEventArgs.Emoji.Url,
            };

            await _dbContext.DiscordReactions.AddAsync(new DiscordReaction
            {
                Message = message,
                User = user,
                Emote = emote,
                IsRemoved = false,
            });

            await _dbContext.SaveChangesAsync();
        }

        private async Task ReactionRemoved(DiscordClient sender, MessageReactionRemoveEventArgs messageReactionRemoveEventArgs)
        {
            _logger.LogInformation($"[Reaction][Delete][{messageReactionRemoveEventArgs.Message.Id}][{messageReactionRemoveEventArgs.Emoji.Name}]");
            var message = await _dbContext.DiscordMessages.SingleOrDefaultAsync(message => message.DiscordId == messageReactionRemoveEventArgs.Message.Id) ?? new DiscordMessage
            {
                DiscordId = messageReactionRemoveEventArgs.Message.Id,
                Author = _dbContext.DiscordUsers.FirstOrDefault(user => user.DiscordId == messageReactionRemoveEventArgs.Message.Author.Id),
                Content = messageReactionRemoveEventArgs.Message.Content,
                HasAttatchment = messageReactionRemoveEventArgs.Message.Attachments.Any(),
            };
            var user = await _dbContext.DiscordUsers.SingleOrDefaultAsync(user => user.DiscordId == messageReactionRemoveEventArgs.User.Id) ?? new DiscordUser
            {
                DiscordId = messageReactionRemoveEventArgs.User.Id,
                Username = messageReactionRemoveEventArgs.User.Username,
                Discriminator = messageReactionRemoveEventArgs.User.Discriminator,
                AvatarUrl = messageReactionRemoveEventArgs.User.AvatarUrl,
                IsBot = messageReactionRemoveEventArgs.User.IsBot,
            };
            var emote = await _dbContext.DiscordEmotes.SingleOrDefaultAsync(emote => emote.DiscordId == messageReactionRemoveEventArgs.Emoji.Id) ?? new DiscordEmote
            {
                DiscordId = messageReactionRemoveEventArgs.Emoji.Id,
                Name = messageReactionRemoveEventArgs.Emoji.Name,
                IsAnimated = messageReactionRemoveEventArgs.Emoji.IsAnimated,
                Url = messageReactionRemoveEventArgs.Emoji.Url,
            };

            try
            {
                var reaction = await _dbContext.DiscordReactions.SingleOrDefaultAsync(r => r.User == user && r.Message == message && r.Emote == emote && !r.IsRemoved);
                reaction.IsRemoved = true;
                _dbContext.DiscordReactions.Update(reaction);
            }
            catch (InvalidOperationException)
            {
                await _dbContext.DiscordReactions.AddAsync(new DiscordReaction
                {
                    Message = message,
                    User = user,
                    Emote = emote,
                    IsRemoved = true,
                });
            }
            await _dbContext.SaveChangesAsync();
        }

        //private async Task ReactionsRemovedForEmote(DiscordClient sender, MessageReactionRemoveEmojiEventArgs messageReactionRemoveEmojiEventArgs)
        //{
        //    _logger.LogInformation($"[Reaction][Delete][{messageReactionRemoveEmojiEventArgs.Message.Id}][{messageReactionRemoveEmojiEventArgs.Emoji.Name}]");
        //    var message = await _dbContext.DiscordMessages.SingleOrDefaultAsync(message => message.DiscordId == messageReactionRemoveEmojiEventArgs.Message.Id);
        //    var emote = await _dbContext.DiscordEmotes.SingleOrDefaultAsync(emote => emote.DiscordId == messageReactionRemoveEmojiEventArgs.Emoji.Id);
        //    var reactions = await _dbContext.DiscordReactions.Where(reaction => reaction.Message == message && reaction.Emote == emote).ToListAsync();
        //    foreach (var reaction in reactions)
        //    {
        //        reaction.IsRemoved = true;
        //    }
        //    _dbContext.DiscordReactions.UpdateRange(reactions);
        //    await _dbContext.SaveChangesAsync();
        //}

        //private async Task ReactionsCleared(DiscordClient sender, MessageReactionsClearEventArgs messageReactionsClearEventArgs)
        //{
        //    _logger.LogInformation($"[Reaction][DeleteAll][{messageReactionsClearEventArgs.Message.Id}]");
        //    var message = await _dbContext.DiscordMessages.SingleOrDefaultAsync(message => message.DiscordId == messageReactionsClearEventArgs.Message.Id);
        //    var reactions = await _dbContext.DiscordReactions.Where(reaction => reaction.Message == message).ToListAsync();
        //    foreach (var reaction in reactions)
        //    {
        //        reaction.IsRemoved = true;
        //    }
        //    _dbContext.DiscordReactions.UpdateRange(reactions);
        //    await _dbContext.SaveChangesAsync();
        //}

        private async Task TypingStarted(DiscordClient sender, TypingStartEventArgs typingStartEventArgs)
        {
            _logger.LogInformation($"[Typing][{typingStartEventArgs.Channel.Id}][{typingStartEventArgs.User.Username}]");
            var user = await _dbContext.DiscordUsers.SingleOrDefaultAsync(user => user.DiscordId == typingStartEventArgs.User.Id);
            var channel = await _dbContext.DiscordTextChannels.SingleOrDefaultAsync(channel => channel.DiscordId == typingStartEventArgs.Channel.Id);
            await _dbContext.DiscordTypingStatuses.AddAsync(new DiscordTypingStatus
            {
                User = user,
                TextChannel = channel,
            });
            await _dbContext.SaveChangesAsync();
        }

        private async Task VoiceStateUpdated(DiscordClient sender, VoiceStateUpdateEventArgs voiceStateUpdateEventArgs)
        {
            _logger.LogInformation("Voice state updated");
            var voiceState = voiceStateUpdateEventArgs.After;
            var user = await _dbContext.DiscordUsers.SingleOrDefaultAsync(user => user.DiscordId == voiceStateUpdateEventArgs.User.Id);
            var channel = await _dbContext.DiscordVoiceChannels.SingleOrDefaultAsync(channel => channel.DiscordId == voiceStateUpdateEventArgs.Channel.Id);
            await _dbContext.DiscordVoiceStatuses.AddAsync(new DiscordVoiceStatus
            {
                User = user,
                VoiceChannel = channel,
                IsSelfMuted = voiceState.IsSelfMuted,
                IsSelfDeafened = voiceState.IsSelfDeafened,
                IsSelfStream = voiceState.IsSelfStream,
                IsSelfVideo = voiceState.IsSelfVideo,
                IsServerMuted = voiceState.IsServerMuted,
                IsServerDeafened = voiceState.IsServerDeafened,
                IsSuppressed = voiceState.IsSuppressed,
            });
            await _dbContext.SaveChangesAsync();
        }

        private async Task PresenceUpdated(DiscordClient sender, PresenceUpdateEventArgs presenceUpdateEventArgs)
        {
            _logger.LogInformation($"[Presence][{presenceUpdateEventArgs.User.Username}]");
            var user = await _dbContext.DiscordUsers.SingleOrDefaultAsync(u =>
                u.DiscordId == presenceUpdateEventArgs.User.Id);
            var presence = presenceUpdateEventArgs.PresenceAfter;

            await _dbContext.DiscordPresenceStatuses.AddAsync(new DiscordPresenceStatus
            {
                User = user,
                Name = presence.Activity.Name,
                Details = presence.Activity.RichPresence.Details,
                Status = (DiscordStatus)presence.Status,
                ActivityType = (DiscordActivityType)presence.Activity.ActivityType,
                State = presence.Activity.RichPresence.State,
                SmallImageText = presence.Activity.RichPresence.SmallImageText,
                LargeImageText = presence.Activity.RichPresence.LargeImageText,
            });

            await _dbContext.SaveChangesAsync();
        }
    }
}
