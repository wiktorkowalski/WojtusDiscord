using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Services
{
    public class GuildInitializerService
    {
        private readonly ILogger<GuildInitializerService> _logger;
        private readonly DiscordUserService _discordUserService;
        private readonly DiscordGuildService _discordGuildService;
        private readonly DiscordGuildMemberService _discordGuildMemberService;
        private readonly DiscordChannelService _discordChannelService;
        private readonly DiscordEmoteService _discordEmoteService;
        private readonly DiscordMessageService _discordMessageService;
        private readonly DiscordReactionService _discordReactionService;

        public GuildInitializerService(
            ILogger<GuildInitializerService> logger,
            DiscordUserService discordUserService,
            DiscordGuildService discordGuildService,
            DiscordGuildMemberService discordGuildMemberService,
            DiscordChannelService discordChannelService,
            DiscordEmoteService discordEmoteService,
            DiscordMessageService discordMessageService,
            DiscordReactionService discordReactionService)
        {
            _logger = logger;
            _discordUserService = discordUserService;
            _discordGuildService = discordGuildService;
            _discordGuildMemberService = discordGuildMemberService;
            _discordChannelService = discordChannelService;
            _discordEmoteService = discordEmoteService;
            _discordMessageService = discordMessageService;
            _discordReactionService = discordReactionService;
        }

        public async Task<DiscordUser[]> CreateUsers(DiscordUser[] users)
        {
            return _discordUserService.CreateMany(users);
        }

        public async Task<DiscordUser> CreateUser(DiscordUser user)
        {
            return _discordUserService.Create(user);
        }

        public async Task<DiscordGuild> CreateGuild(DiscordGuild guild)
        {
            return _discordGuildService.Create(guild);
        }

        public async Task<DiscordChannel[]> CreateChannels(DiscordChannel[] channels)
        {
            return _discordChannelService.CreateMany(channels);
        }
        
        public async Task<DiscordEmote[]> CreateEmotes(DiscordEmote[] emotes)
        {
            return _discordEmoteService.CreateMany(emotes);
        }

        public async Task<DiscordEmote> CreateEmote(DiscordEmote emote)
        {
            return _discordEmoteService.Create(emote);
        }

        public async Task<DiscordMessage[]> CreateMessages(DiscordMessage[] messages)
        {
            return _discordMessageService.CreateMany(messages);
        }

        public async Task<DiscordMessage> CreateMessage(DiscordMessage message)
        {
            return _discordMessageService.Create(message);
        }

        public async Task<DiscordGuildMember[]> CreateGuildMembers(DiscordGuild guild, DiscordUser[] users)
        {
            return _discordGuildMemberService.CreateMany(guild, users);
        }
    }
}
