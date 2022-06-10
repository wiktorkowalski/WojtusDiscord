using Discord;
using Discord.WebSocket;

namespace WojtusDiscord.ArchiveService
{
    public class DiscordEventsHandlers
    {
        private readonly ILogger<DiscordEventsHandlers> _logger;
        private readonly DatabaseProvider _databaseProvider;

        public DiscordEventsHandlers(ILogger<DiscordEventsHandlers> logger, DatabaseProvider databaseProvider)
        {
            _logger = logger;
            _databaseProvider = databaseProvider;
        }

        public void AssignEventHandlers(DiscordSocketClient discordSocketClient)
        {
            discordSocketClient.MessageReceived += OnMessageReceived;
            discordSocketClient.MessageDeleted += OnMessageDeleted;
            discordSocketClient.MessageUpdated += OnMessageUpdated;
            discordSocketClient.ReactionAdded += OnReactionAdded;
            discordSocketClient.ReactionRemoved += OnReactionRemoved;
            discordSocketClient.ReactionsCleared += OnReactionsCleared;
            discordSocketClient.ReactionsRemovedForEmote += OnReactionsRemovedForEmote;
            discordSocketClient.UserJoined += OnUserJoined;
            discordSocketClient.UserLeft += OnUserLeft;
            discordSocketClient.UserBanned += OnUserBanned;
            discordSocketClient.UserUnbanned += OnUserUnbanned;
            discordSocketClient.UserUpdated += OnUserUpdated;
            discordSocketClient.UserIsTyping += OnUserIsTyping;
            discordSocketClient.UserVoiceStateUpdated += OnUserVoiceStateUpdated;
            discordSocketClient.PresenceUpdated += OnPresenceUpdated;
        }


        #region Event Handlers
        private Task OnMessageReceived(SocketMessage arg)
        {
            _logger.LogInformation($"[Message][{arg.Channel.Name}]{arg.Author.Username}:{arg.Content}");
            _databaseProvider.InsertActivity(new Activity
            {
                Message = arg.Content,
                Channel = arg.Channel.Name,
                User = arg.Author.Username,
                ActivityType = ActivityType.Message
            });
            return Task.CompletedTask;
        }

        private Task OnMessageDeleted(Cacheable<IMessage, ulong> arg, Cacheable<IMessageChannel, ulong> arg2)
        {
            return Task.CompletedTask;
        }
        private Task OnMessageUpdated(Cacheable<IMessage, ulong> arg, SocketMessage message, ISocketMessageChannel arg2)
        {
            return Task.CompletedTask;
        }
        private Task OnReactionAdded(Cacheable<IUserMessage, ulong> arg, Cacheable<IMessageChannel, ulong> arg2, SocketReaction reaction)
        {
            return Task.CompletedTask;
        }
        private Task OnReactionRemoved(Cacheable<IUserMessage, ulong> arg, Cacheable<IMessageChannel, ulong> arg2, SocketReaction reaction)
        {
            return Task.CompletedTask;
        }
        private Task OnReactionsCleared(Cacheable<IUserMessage, ulong> arg, Cacheable<IMessageChannel, ulong> arg2)
        {
            return Task.CompletedTask;
        }
        private Task OnReactionsRemovedForEmote(Cacheable<IUserMessage, ulong> arg, Cacheable<IMessageChannel, ulong> arg2, IEmote emote)
        {
            return Task.CompletedTask;
        }
        private Task OnUserJoined(SocketGuildUser user)
        {
            return Task.CompletedTask;
        }
        private Task OnUserLeft(SocketGuild guild, SocketUser user)
        {
            return Task.CompletedTask;
        }
        private Task OnUserBanned(SocketUser user, SocketGuild guild)
        {
            return Task.CompletedTask;
        }
        private Task OnUserUnbanned(SocketUser user, SocketGuild guild)
        {
            return Task.CompletedTask;
        }
        private Task OnUserUpdated(SocketUser userPast, SocketUser userNow)
        {
            return Task.CompletedTask;
        }
        private Task OnUserIsTyping(Cacheable<IUser, ulong> arg, Cacheable<IMessageChannel, ulong> arg2)
        {
            return Task.CompletedTask;
        }
        private Task OnUserVoiceStateUpdated(SocketUser user, SocketVoiceState socketVoiceStatePast, SocketVoiceState socketVoiceStateNow)
        {
            return Task.CompletedTask;
        }
        private Task OnPresenceUpdated(SocketUser user, SocketPresence presencePast, SocketPresence presenceNow)
        {
            return Task.CompletedTask;
        }

        #endregion
    }
}
