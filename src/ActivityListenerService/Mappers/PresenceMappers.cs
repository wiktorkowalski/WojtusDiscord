using ActivityListenerService.Models;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DiscordActivity = ActivityListenerService.Models.DiscordActivity;

namespace ActivityListenerService.Mappers;

public static class PresenceMappers
{
    public static DiscordPresenceStatus MapToDiscordPresenceStatus(this PresenceUpdateEventArgs args)
    {
        return new DiscordPresenceStatus
        {
            UserId = args.User.Id,
            Before = args.PresenceBefore.MapToDiscordPresenceStatusDetails(),
            After = args.PresenceAfter.MapToDiscordPresenceStatusDetails()
        };
    }
    
    public static DiscordPresenceStatusDetails MapToDiscordPresenceStatusDetails(this DiscordPresence presence)
    {
        return new DiscordPresenceStatusDetails
        {
            Status = (DiscordStatus)presence.Status,
            Activities = presence.Activities.Select(a => a.MapToDiscordActivity()).ToList()
        };
    }
    
    public static DiscordActivity MapToDiscordActivity(this DSharpPlus.Entities.DiscordActivity activity)
    {
        return new DiscordActivity
        {
            Name = activity.Name,
            ActivityType = (DiscordActivityType)activity.ActivityType,
            Start = activity.RichPresence?.StartTimestamp?.UtcDateTime ?? null,
            End = activity.RichPresence?.EndTimestamp?.UtcDateTime ?? null,
            LargeImage = activity.RichPresence?.LargeImage?.Id ?? null,
            LargeImageText = activity.RichPresence?.LargeImageText ?? null,
            SmallImage = activity.RichPresence?.SmallImage?.Id ?? null,
            SmallImageText = activity.RichPresence?.SmallImageText ?? null,
            Details = activity.RichPresence?.Details ?? null,
            State = activity.RichPresence?.State ?? null,
            ApplicationId = activity.RichPresence?.Application?.Id.ToString() ?? null,
            Party = activity.RichPresence?.PartyId?.ToString() ?? null,
            EmoteId = activity.CustomStatus?.Emoji?.Id ?? null,
        };
    }
}