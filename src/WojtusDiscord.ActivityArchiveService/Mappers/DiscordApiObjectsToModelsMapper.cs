using Nelibur.ObjectMapper;

namespace WojtusDiscord.ActivityArchiveService.Mappers;

public static class DiscordApiObjectsToModelsMapper
{
    public static void InitializeMappings()
    {
        TinyMapper.Bind<DSharpPlus.Entities.DiscordUser, Models.DiscordUser>(
            config =>
            {
                config.Bind(source => source.Id, target => target.DiscordId);
                config.Bind(source => source.Username, target => target.Username);
                config.Bind(source => source.Discriminator, target => target.Discriminator);
                config.Bind(source => source.AvatarUrl, target => target.AvatarUrl);
                config.Bind(source => source.IsBot, target => target.IsBot);
            });

        TinyMapper.Bind<DSharpPlus.Entities.DiscordMember, Models.DiscordUser>(
            config =>
            {
                config.Bind(source => source.Id, target => target.DiscordId);
                config.Bind(source => source.Username, target => target.Username);
                config.Bind(source => source.Discriminator, target => target.Discriminator);
                config.Bind(source => source.AvatarUrl, target => target.AvatarUrl);
                config.Bind(source => source.IsBot, target => target.IsBot);
            });

        TinyMapper.Bind<DSharpPlus.Entities.DiscordGuild, Models.DiscordGuild>(
            config =>
            {
                config.Bind(source => source.Id, target => target.DiscordId);
                config.Bind(source => source.Name, target => target.Name);
                config.Bind(source => source.IconUrl, target => target.IconUrl);
            });

        TinyMapper.Bind<DSharpPlus.Entities.DiscordChannel, Models.DiscordTextChannel>(
            config =>
            {
                config.Bind(source => source.Id, target => target.DiscordId);
                config.Bind(source => source.Name, target => target.Name);
                config.Bind(source => source.Topic, target => target.Topic);
            });

        TinyMapper.Bind<DSharpPlus.Entities.DiscordChannel, Models.DiscordVoiceChannel>(
            config =>
            {
                config.Bind(source => source.Id, target => target.DiscordId);
                config.Bind(source => source.Name, target => target.Name);
                config.Bind(source => source.GuildId, target => target.GuildId);
                config.Bind(source => source.Bitrate, target => target.BitRate);
                config.Bind(source => source.UserLimit, target => target.UserLimit);
                config.Bind(source => source.RtcRegion, target => target.RtcRegion);
            });

        TinyMapper.Bind<DSharpPlus.Entities.DiscordMessage, Models.DiscordMessage>(
            config =>
            {
                config.Bind(source => source.Id, target => target.DiscordId);
                config.Bind(source => source.Content, target => target.Content);
                config.Bind(source => source.Attachments, target => target.HasAttatchment);
            });

        TinyMapper.Bind<DSharpPlus.Entities.DiscordEmoji, Models.DiscordEmote>(
            config =>
            {
                config.Bind(source => source.Id, target => target.DiscordId);
                config.Bind(source => source.Name, target => target.Name);
                config.Bind(source => source.IsAnimated, target => target.IsAnimated);
                
            });
    }
}