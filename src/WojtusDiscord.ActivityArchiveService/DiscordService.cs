using System.Text.Json;
using Discord;
using Discord.WebSocket;

namespace WojtusDiscord.ActivityArchiveService
{
    public class DiscordService : IHostedService
    {
        private readonly ILogger<DiscordService> _logger;
        private readonly DiscordSocketClient _discordClient;
        private readonly string _token;

        public DiscordService(ILogger<DiscordService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _discordClient = new DiscordSocketClient();
            _token = configuration.GetValue<string>("DiscordTokenDev");

            //TODO: move to separate service
            _discordClient.MessageReceived += MessageReceived;
            _discordClient.MessageUpdated += MessageUpdated;
            _discordClient.MessageDeleted += MessageDeleted;
            _discordClient.ReactionAdded += ReactionAdded;
            _discordClient.ReactionRemoved += ReactionRemoved;
            _discordClient.ReactionsRemovedForEmote += ReactionsRemovedForEmote;
            _discordClient.ReactionsCleared += ReactionsCleared;
            _discordClient.UserIsTyping += UserIsTyping;
            _discordClient.UserVoiceStateUpdated += UserVoiceStateUpdated;
            _discordClient.PresenceUpdated += PresenceUpdated;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting DiscordService...");
            await _discordClient.LoginAsync(TokenType.Bot, _token);
            await _discordClient.StartAsync();
            _logger.LogInformation("Started DiscordService");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping DiscordService...");
            await _discordClient.LogoutAsync();
            _logger.LogInformation("Stopped DiscordService");
        }

        private async Task MessageReceived(SocketMessage socketMessage)
        {
            _logger.LogInformation($"[Message][{socketMessage.Id}][{socketMessage.Author.Username}]");
            
        }

        private async Task MessageUpdated(Cacheable<IMessage, ulong> cacheable, SocketMessage socketMessage, ISocketMessageChannel socketMessageChannel)
        {
            _logger.LogInformation("Message updated");
            _logger.LogInformation(JsonSerializer.Serialize(cacheable));
            _logger.LogInformation(JsonSerializer.Serialize(socketMessage));
            _logger.LogInformation(JsonSerializer.Serialize(socketMessageChannel));
        }

        private async Task MessageDeleted(Cacheable<IMessage, ulong> cacheable, Cacheable<IMessageChannel, ulong> cacheable1)
        {
            _logger.LogInformation("Message deleted");
            _logger.LogInformation(JsonSerializer.Serialize(cacheable));
            _logger.LogInformation(JsonSerializer.Serialize(cacheable1));
        }

        private async Task ReactionAdded(Cacheable<IUserMessage, ulong> cacheable, Cacheable<IMessageChannel, ulong> cacheable1, SocketReaction socketReaction)
        {
            _logger.LogInformation("Reaction added");
            _logger.LogInformation(JsonSerializer.Serialize(cacheable));
            _logger.LogInformation(JsonSerializer.Serialize(cacheable1));
            _logger.LogInformation(JsonSerializer.Serialize(socketReaction));
        }

        private async Task ReactionRemoved(Cacheable<IUserMessage, ulong> cacheable, Cacheable<IMessageChannel, ulong> cacheable1, SocketReaction socketReaction)
        {
            _logger.LogInformation("Reaction removed");
            _logger.LogInformation(JsonSerializer.Serialize(cacheable));
            _logger.LogInformation(JsonSerializer.Serialize(cacheable1));
            _logger.LogInformation(JsonSerializer.Serialize(socketReaction));
        }

        private async Task ReactionsRemovedForEmote(Cacheable<IUserMessage, ulong> cacheable, Cacheable<IMessageChannel, ulong> cacheable1, IEmote iEmote)
        {
            _logger.LogInformation("Reactions removed for emote");
            _logger.LogInformation(JsonSerializer.Serialize(cacheable));
            _logger.LogInformation(JsonSerializer.Serialize(cacheable1));
            _logger.LogInformation(JsonSerializer.Serialize(iEmote));
        }

        private async Task ReactionsCleared(Cacheable<IUserMessage, ulong> cacheable, Cacheable<IMessageChannel, ulong> cacheable1)
        {
            _logger.LogInformation("Reactions cleared");
            _logger.LogInformation(JsonSerializer.Serialize(cacheable));
            _logger.LogInformation(JsonSerializer.Serialize(cacheable1));
        }

        private async Task UserIsTyping(Cacheable<IUser, ulong> cacheable, Cacheable<IMessageChannel, ulong> cacheable1)
        {
            _logger.LogInformation("User is typing");
            _logger.LogInformation(JsonSerializer.Serialize(cacheable));
            _logger.LogInformation(JsonSerializer.Serialize(cacheable1));
        }

        private async Task UserVoiceStateUpdated(SocketUser socketUser, SocketVoiceState socketVoiceState, SocketVoiceState socketVoiceState2)
        {
            _logger.LogInformation("Voice state updated");
            _logger.LogInformation(JsonSerializer.Serialize(socketUser));
            _logger.LogInformation(JsonSerializer.Serialize(socketVoiceState));
            _logger.LogInformation(JsonSerializer.Serialize(socketVoiceState2));
        }

        private async Task PresenceUpdated(SocketUser arg1, SocketPresence arg2, SocketPresence arg3)
        {
            _logger.LogInformation("Presence Updated");
        }
    }
}
