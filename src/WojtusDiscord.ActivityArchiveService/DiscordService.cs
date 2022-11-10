using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using Nelibur.ObjectMapper;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Models;
using WojtusDiscord.ActivityArchiveService.Services;

namespace WojtusDiscord.ActivityArchiveService
{
    //TODO: add mappers
    public class DiscordService : IHostedService
    {
        private readonly ILogger<DiscordService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DiscordClient _discordClient;
        private readonly ActivityArchiveContext _dbContext;
        
        public DiscordService(ILogger<DiscordService> logger, IConfiguration configuration, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _discordClient = new DiscordClient(new DiscordConfiguration
            {
                Token = configuration.GetValue<string>("DiscordToken"),
                Intents = DiscordIntents.All,
                //TODO: use logger configuration from DI
                LoggerFactory = new LoggerFactory().AddSerilog(new LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .WriteTo.Console(theme: AnsiConsoleTheme.Code, outputTemplate: "[{Timestamp:HH:mm:ss}|{Level:u4}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger()),
            });
            //TODO: properly use context
            _dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ActivityArchiveContext>();

            //TODO: move to separate service
            _discordClient.MessageCreated += MessageCreated;
            _discordClient.MessageUpdated += MessageUpdated;
            _discordClient.MessageDeleted += MessageDeleted;
            _discordClient.MessagesBulkDeleted += MessageBuldDeleted;
            
            _discordClient.MessageReactionAdded += ReactionAdded;
            _discordClient.MessageReactionRemoved += ReactionRemoved;
            _discordClient.MessageReactionRemovedEmoji += ReactionsRemovedForEmote;
            _discordClient.MessageReactionsCleared += ReactionsCleared;
            
            _discordClient.TypingStarted += TypingStarted;
            _discordClient.VoiceStateUpdated += VoiceStateUpdated;
            
            _discordClient.PresenceUpdated += PresenceUpdated;
            
            _discordClient.GuildCreated += JoinedGuild;
        }

        private async Task JoinedGuild(DiscordClient sender, GuildCreateEventArgs guildCreateEventArgs)
        {
            _logger.LogInformation($"[Guild][{guildCreateEventArgs.Guild.Name}]");
            var guild = guildCreateEventArgs.Guild;
            await _dbContext.DiscordUsers.AddRangeAsync(guild.Members.Select(u => TinyMapper.Map<DiscordUser>(u.Value)));
            await _dbContext.SaveChangesAsync();
            var owner = await _dbContext.DiscordUsers.SingleOrDefaultAsync(u => u.DiscordId == guild.OwnerId);
            var guildToSave = TinyMapper.Map<DiscordGuild>(guild);
            guildToSave.Owner = owner;
            var guildEntry = _dbContext.DiscordGuilds.Add(guildToSave);
            //var guildEntry = await _dbContext.DiscordGuilds.AddAsync(new DiscordGuild
            //{
            //    Name = guild.Name,
            //    DiscordId = guild.Id,
            //    IconUrl = guild.IconUrl,
            //    Owner = owner ?? new DiscordUser
            //    {
            //        DiscordId = guild.Owner.Id,
            //        Username = guild.Owner.Username,
            //        Discriminator = guild.Owner.Discriminator,
            //        AvatarUrl = guild.Owner.AvatarUrl,
            //        IsBot = guild.Owner.IsBot,
            //    },
            //});
            await _dbContext.SaveChangesAsync();
            await _dbContext.DiscordGuildMembers.AddRangeAsync(guild.Members.Select(u => new DiscordGuildMember
            {
                DiscordGuild = guildEntry.Entity,
                DiscordUser = _dbContext.DiscordUsers.First(user => user.DiscordId == u.Value.Id),
            }));

            await _dbContext.DiscordTextChannels.AddRangeAsync(guild.Channels
                .Where(c => c.Value.Type == ChannelType.Text)
                .Select(c => new DiscordTextChannel
            {
                DiscordId = c.Value.Id,
                Name = c.Value.Name,
                Guild = guildEntry.Entity,
            }));
            await _dbContext.DiscordVoiceChannels.AddRangeAsync(guild.Channels
                .Where(c => c.Value.Type == ChannelType.Voice)
                .Select(c => new DiscordVoiceChannel
            {
                DiscordId = c.Value.Id,
                Name = c.Value.Name,
                Guild = guildEntry.Entity,
            }));
            await _dbContext.DiscordEmotes.AddRangeAsync(guild.Emojis.Select(e => new DiscordEmote
            {
                DiscordId = e.Value.Id,
                Name = e.Value.Name,
                IsAnimated = e.Value.IsAnimated,
                Url = e.Value.Url,
            }));

            await _dbContext.SaveChangesAsync();
            return;

            //get last 1000 messages from each channel, should be enough for now
            foreach (var textChannel in guild.Channels.Values.Where(c => c.Type == ChannelType.Text))
            {
                var messages = (await textChannel.GetMessagesAsync(1000)).OrderBy(m => m.Timestamp).ToArray();
                _logger.LogInformation($"Got {messages.Length} messages from {textChannel.Name}");
                //save messages
                foreach (var message in messages)
                {
                    var author = _dbContext.DiscordUsers.FirstOrDefault(u => u.DiscordId == message.Author.Id) ?? new DiscordUser
                    {
                        DiscordId = message.Author.Id,
                        Username = message.Author.Username,
                        Discriminator = message.Author.Discriminator,
                        AvatarUrl = message.Author.AvatarUrl,
                        IsBot = message.Author.IsBot,
                        IsWebhook = message.WebhookMessage,
                    };
                    var channel = _dbContext.DiscordTextChannels.First(c => c.DiscordId == textChannel.Id);
                    await _dbContext.DiscordMessages.AddAsync(new DiscordMessage
                    {
                        DiscordId = message.Id,
                        Author = author,
                        TextChannel = channel,
                        Content = message.Content,
                        
                    });
                }
                //await _dbContext.DiscordMessages.AddRangeAsync(messages.Select(m => new DiscordMessage
                //{
                //    DiscordId = m.Id,
                //    Author = _dbContext.DiscordUsers.First(u => u.DiscordId == m.Author.Id),
                //    TextChannel = _dbContext.DiscordTextChannels.First(c => c.DiscordId == textChannel.Id),
                //    Content = m.Content,
                //    HasAttatchment = m.Attachments.Any(),
                //}));
            }
        }

        private async Task JoinedGuildNew(DiscordClient sender, GuildCreateEventArgs guildCreateEventArgs)
        {
            _logger.LogInformation($"[Guild][Join][{guildCreateEventArgs.Guild.Name}]");
            using var scope = _scopeFactory.CreateScope();
            var guildInitService = scope.ServiceProvider.GetRequiredService<GuildInitializerService>();

            guildInitService.CreateUsers(guildCreateEventArgs.Guild.Members.Values.Select(m => TinyMapper.Map<DiscordUser>(m)).ToArray());
            guildInitService.CreateGuild(TinyMapper.Map<DiscordGuild>(guildCreateEventArgs.Guild));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting DiscordService...");
            await _discordClient.ConnectAsync();
            _logger.LogInformation("Started DiscordService");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping DiscordService...");
            await _discordClient.DisconnectAsync();
            _logger.LogInformation("Stopped DiscordService");
        }

        private async Task MessageCreated(DiscordClient sender, MessageCreateEventArgs messageCreateEventArgs)
        {
            _logger.LogInformation($"[Message][{messageCreateEventArgs.Message.Id}][{messageCreateEventArgs.Author.Username}]");
            var author = await _dbContext.DiscordUsers.SingleOrDefaultAsync(user => user.DiscordId == messageCreateEventArgs.Author.Id);
            var channel = await _dbContext.DiscordTextChannels.SingleOrDefaultAsync(channel => channel.DiscordId == messageCreateEventArgs.Channel.Id);
            var guild = await _dbContext.DiscordGuilds.FirstOrDefaultAsync(guild => guild.DiscordId == messageCreateEventArgs.Guild.Id);

            await _dbContext.DiscordMessages.AddAsync(new DiscordMessage
            {
                DiscordId = messageCreateEventArgs.Message.Id,
                Author = author ?? new DiscordUser
                {
                    DiscordId = messageCreateEventArgs.Author.Id,
                    Username = messageCreateEventArgs.Author.Username,
                    Discriminator = messageCreateEventArgs.Author.Discriminator,
                    AvatarUrl = messageCreateEventArgs.Author.AvatarUrl,
                    IsBot = messageCreateEventArgs.Author.IsBot,
                },
                TextChannel = channel ?? new DiscordTextChannel
                {
                    DiscordId = messageCreateEventArgs.Channel.Id,
                    Name = messageCreateEventArgs.Channel.Name,
                    Guild = guild,
                },
                Content = messageCreateEventArgs.Message.Content,
                HasAttatchment = messageCreateEventArgs.Message.Attachments.Any(),
            });
            await _dbContext.SaveChangesAsync();
        }

        private async Task MessageUpdated(DiscordClient sender, MessageUpdateEventArgs messageUpdateEventArgs)
        {
            _logger.LogInformation($"[Message][Update][{messageUpdateEventArgs.Message.Id}][{messageUpdateEventArgs.Author.Username}]");
            var message = await _dbContext.DiscordMessages.SingleOrDefaultAsync(message => message.DiscordId == messageUpdateEventArgs.Message.Id);
            message.Content = messageUpdateEventArgs.Message.Content;
            message.IsEdited = true;
            await _dbContext.DiscordMessageContentEdit.AddAsync(new DiscordMessageContentEdit
            {
                Message = message,
                IsRemoved = false,
                Content = messageUpdateEventArgs.Message.Content,
                ContentBefore = messageUpdateEventArgs.MessageBefore.Content,
            });
            await _dbContext.SaveChangesAsync();
        }

        private async Task MessageDeleted(DiscordClient sender, MessageDeleteEventArgs messageDeleteEventArgs)
        {
            _logger.LogInformation($"[Message][Delete][{messageDeleteEventArgs.Message.Id}][{messageDeleteEventArgs.Message.Author.Username}]");
            var message = await _dbContext.DiscordMessages.FirstOrDefaultAsync(message => message.DiscordId == messageDeleteEventArgs.Message.Id);
            message.IsRemoved = true;
            await _dbContext.DiscordMessageContentEdit.AddAsync(new DiscordMessageContentEdit
            {
                Message = message,
                IsRemoved = true,
                Content = null,
                ContentBefore = messageDeleteEventArgs.Message.Content
            });
            await _dbContext.SaveChangesAsync();
        }

        private async Task MessageBuldDeleted(DiscordClient sender, MessageBulkDeleteEventArgs messageBulkDeleteEventArgs)
        {
            _logger.LogInformation("[Message][BulkDelete]");
            var messages = await _dbContext.DiscordMessages.Where(message => messageBulkDeleteEventArgs.Messages.Any(m => m.Id == message.DiscordId)).ToListAsync();
            foreach (var message in messages)
            {
                message.IsRemoved = true;
                await _dbContext.DiscordMessageContentEdit.AddAsync(new DiscordMessageContentEdit
                {
                    Message = message,
                    IsRemoved = true,
                    Content = null,
                    ContentBefore = message.Content
                });
            }
            await _dbContext.SaveChangesAsync();
        }

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

        private async Task ReactionsRemovedForEmote(DiscordClient sender, MessageReactionRemoveEmojiEventArgs messageReactionRemoveEmojiEventArgs)
        {
            _logger.LogInformation($"[Reaction][Delete][{messageReactionRemoveEmojiEventArgs.Message.Id}][{messageReactionRemoveEmojiEventArgs.Emoji.Name}]");
            var message = await _dbContext.DiscordMessages.SingleOrDefaultAsync(message => message.DiscordId == messageReactionRemoveEmojiEventArgs.Message.Id);
            var emote = await _dbContext.DiscordEmotes.SingleOrDefaultAsync(emote => emote.DiscordId == messageReactionRemoveEmojiEventArgs.Emoji.Id);
            var reactions = await _dbContext.DiscordReactions.Where(reaction => reaction.Message == message && reaction.Emote == emote).ToListAsync();
            foreach (var reaction in reactions)
            {
                reaction.IsRemoved = true;
            }
            _dbContext.DiscordReactions.UpdateRange(reactions);
            await _dbContext.SaveChangesAsync();
        }

        private async Task ReactionsCleared(DiscordClient sender, MessageReactionsClearEventArgs messageReactionsClearEventArgs)
        {
            _logger.LogInformation($"[Reaction][DeleteAll][{messageReactionsClearEventArgs.Message.Id}]");
            var message = await _dbContext.DiscordMessages.SingleOrDefaultAsync(message => message.DiscordId == messageReactionsClearEventArgs.Message.Id);
            var reactions = await _dbContext.DiscordReactions.Where(reaction => reaction.Message == message).ToListAsync();
            foreach (var reaction in reactions)
            {
                reaction.IsRemoved = true;
            }
            _dbContext.DiscordReactions.UpdateRange(reactions);
            await _dbContext.SaveChangesAsync();
        }

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
                IsSelfDeafened = voiceState.IsSelfDeafened ,
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
