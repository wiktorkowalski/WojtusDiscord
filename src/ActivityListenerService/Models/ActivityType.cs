namespace ActivityListenerService.Models;

public enum ActivityType
{
    MessageCreated,
    MessageUpdated,
    MessageDeleted,
    MessageBuldDeleted,
    ReactionAdded,
    ReactionRemoved,
    ReactionsRemovedForEmote,
    ReactionsCleared,
    TypingStarted,
    VoiceStateUpdated,
    PresenceUpdated,
    JoinedGuild,
    GuildMemberAdded,
    GuildMemberUpdated,
    ChannelCreated,
    ChannelUpdated,
}