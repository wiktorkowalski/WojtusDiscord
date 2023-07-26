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
            DesktopStatus = (DiscordStatus)presence.ClientStatus.Desktop.Value,
            MobileStatus = (DiscordStatus)presence.ClientStatus.Mobile.Value,
            WebStatus = (DiscordStatus)presence.ClientStatus.Web.Value,
            Activities = presence.Activities.Select(a => a.MapToDiscordActivity()).ToList()
        };
    }
    
    public static DiscordActivity MapToDiscordActivity(this DSharpPlus.Entities.DiscordActivity activity)
    {
        return new DiscordActivity
        {
            Name = activity.Name,
            ActivityType = (DiscordActivityType)activity.ActivityType,
            Start = activity.RichPresence?.StartTimestamp?.UtcDateTime,
            End = activity.RichPresence?.EndTimestamp?.UtcDateTime,
            LargeImage = activity.RichPresence?.LargeImage.Id,
            LargeImageText = activity.RichPresence?.LargeImageText,
            SmallImage = activity.RichPresence?.SmallImage.Id,
            SmallImageText = activity.RichPresence?.SmallImageText,
            Details = activity.RichPresence?.Details,
            State = activity.RichPresence?.State,
            ApplicationId = activity.RichPresence?.Application.Id.ToString(),
            Party = activity.RichPresence?.PartyId.ToString(),
            EmoteId = activity.CustomStatus?.Emoji?.Id ?? null,
        };
    }
}