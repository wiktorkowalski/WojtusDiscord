using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace DiscordEventService.Services.EventHandlers;

public class PresenceEventHandler(IServiceScopeFactory scopeFactory, ILogger<PresenceEventHandler> logger) :
    IEventHandler<PresenceUpdatedEventArgs>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task HandleEventAsync(DiscordClient sender, PresenceUpdatedEventArgs args)
    {
        try
        {
            var now = DateTime.UtcNow;

            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                args, "PresenceUpdated", 0, null, args.User.Id);

            // Record the presence event
            var presenceEvent = new PresenceEventEntity
            {
                UserDiscordId = args.User.Id,
                GuildDiscordId = 0,

                DesktopStatusBefore = GetStatusValue(args.PresenceBefore?.ClientStatus?.Desktop),
                MobileStatusBefore = GetStatusValue(args.PresenceBefore?.ClientStatus?.Mobile),
                WebStatusBefore = GetStatusValue(args.PresenceBefore?.ClientStatus?.Web),

                DesktopStatusAfter = GetStatusValue(args.PresenceAfter?.ClientStatus?.Desktop),
                MobileStatusAfter = GetStatusValue(args.PresenceAfter?.ClientStatus?.Mobile),
                WebStatusAfter = GetStatusValue(args.PresenceAfter?.ClientStatus?.Web),

                ActivitiesBeforeJson = SerializeActivities(args.PresenceBefore?.Activities),
                ActivitiesAfterJson = SerializeActivities(args.PresenceAfter?.Activities),

                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            await dbContext.PresenceEvents.AddAsync(presenceEvent);

            // Track activities in ActivityEntity table
            await UpdateActivityTracking(dbContext, args.User.Id, args.PresenceBefore?.Activities, args.PresenceAfter?.Activities, now);

            await dbContext.SaveChangesAsync();

            logger.LogDebug("Recorded presence event for user {UserId}", args.User.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling presence update for user {UserId}", args.User.Id);
        }
    }

    private async Task UpdateActivityTracking(
        DiscordDbContext dbContext,
        ulong userId,
        IReadOnlyList<DiscordActivity>? activitiesBefore,
        IReadOnlyList<DiscordActivity>? activitiesAfter,
        DateTime now)
    {
        // Find activities that ended (were in before but not in after)
        var endedActivities = activitiesBefore?.Where(b =>
            !activitiesAfter?.Any(a => IsSameActivity(a, b)) ?? true) ?? Enumerable.Empty<DiscordActivity>();

        // Find activities that started (are in after but not in before)
        var startedActivities = activitiesAfter?.Where(a =>
            !activitiesBefore?.Any(b => IsSameActivity(a, b)) ?? true) ?? Enumerable.Empty<DiscordActivity>();

        // Mark ended activities
        foreach (var ended in endedActivities)
        {
            var existingActivity = await dbContext.Activities
                .Where(a => a.UserDiscordId == userId && a.IsActive && a.Name == ended.Name && a.ActivityType == (int)ended.ActivityType)
                .FirstOrDefaultAsync();

            if (existingActivity != null)
            {
                existingActivity.IsActive = false;
                existingActivity.EndedAtUtc = now;
                existingActivity.LastSeenAtUtc = now;
            }
        }

        // Add new activities
        foreach (var started in startedActivities)
        {
            var activity = new ActivityEntity
            {
                UserDiscordId = userId,
                ActivityType = (int)started.ActivityType,
                Name = started.Name,
                StreamUrl = started.StreamUrl,
                IsActive = true,
                FirstSeenAtUtc = now,
                LastSeenAtUtc = now
            };

            // For Spotify/Listening activities, try to get additional data
            if (started is { ActivityType: DiscordActivityType.ListeningTo, Name: "Spotify" })
            {
                activity.SpotifySongTitle = started.Name;
            }

            await dbContext.Activities.AddAsync(activity);
        }

        // Update existing activities that are still active
        foreach (var current in activitiesAfter ?? Enumerable.Empty<DiscordActivity>())
        {
            if (activitiesBefore?.Any(b => IsSameActivity(current, b)) == true)
            {
                var existingActivity = await dbContext.Activities
                    .Where(a => a.UserDiscordId == userId && a.IsActive && a.Name == current.Name && a.ActivityType == (int)current.ActivityType)
                    .FirstOrDefaultAsync();

                if (existingActivity != null)
                {
                    existingActivity.LastSeenAtUtc = now;
                }
            }
        }
    }

    private static bool IsSameActivity(DiscordActivity a, DiscordActivity b)
    {
        return a.Name == b.Name && a.ActivityType == b.ActivityType;
    }

    private static int GetStatusValue(Optional<DiscordUserStatus>? status)
    {
        if (status?.HasValue == true)
            return (int)status.Value.Value;
        return (int)DiscordUserStatus.Offline;
    }

    private static string? SerializeActivities(IReadOnlyList<DiscordActivity>? activities)
    {
        if (activities == null || activities.Count == 0)
            return null;

        // Serialize the core activity data that's definitely available
        var activityData = activities.Select(a => new
        {
            Name = a.Name,
            Type = (int)a.ActivityType,
            StreamUrl = a.StreamUrl
        }).ToList();

        return JsonSerializer.Serialize(activityData, JsonOptions);
    }
}
