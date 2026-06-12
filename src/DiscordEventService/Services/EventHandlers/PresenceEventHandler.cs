using System.Text.Json;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class PresenceEventHandler(EventPipeline pipeline) :
    IEventHandler<PresenceUpdatedEventArgs>
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task HandleEventAsync(DiscordClient sender, PresenceUpdatedEventArgs args)
    {
        var guildId = args.PresenceAfter?.Guild?.Id ?? args.PresenceBefore?.Guild?.Id ?? 0;

        // Serialize a DTO rather than the raw event args — DSharpPlus internals don't serialize
        // cleanly, and the full presence payload is noisy. The pipeline serializes whatever we
        // pass as the event object, so this DTO becomes the raw_event_logs row.
        var eventDto = new
        {
            UserId = args.User.Id,
            GuildId = guildId,
            Before = args.PresenceBefore == null ? null : new
            {
                Desktop = GetStatusValue(args.PresenceBefore.ClientStatus?.Desktop),
                Mobile = GetStatusValue(args.PresenceBefore.ClientStatus?.Mobile),
                Web = GetStatusValue(args.PresenceBefore.ClientStatus?.Web),
                Activities = args.PresenceBefore.Activities?.Select(a => new { a.Name, Type = (int)a.ActivityType, a.StreamUrl })
            },
            After = args.PresenceAfter == null ? null : new
            {
                Desktop = GetStatusValue(args.PresenceAfter.ClientStatus?.Desktop),
                Mobile = GetStatusValue(args.PresenceAfter.ClientStatus?.Mobile),
                Web = GetStatusValue(args.PresenceAfter.ClientStatus?.Web),
                Activities = args.PresenceAfter.Activities?.Select(a => new { a.Name, Type = (int)a.ActivityType, a.StreamUrl })
            }
        };

        await pipeline.Execute(eventDto, "PresenceUpdated", nameof(PresenceEventHandler),
            guildId, null, args.User.Id, async ctx =>
            {
                // Presence row, the user upsert, and activity tracking are one logical event:
                // wrap them in a single transaction so a mid-flight failure can't leave a presence
                // record with no activity tracking. ExecutionStrategy is required because
                // EnableRetryOnFailure is configured; the top-of-delegate ChangeTracker.Clear keeps a
                // retry from re-staging this attempt's entities. UpsertUserAsync runs inside the
                // ambient transaction (it never opens its own), so the user update + name history
                // commit atomically with the presence and activity rows.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                    ctx.Db.PresenceEvents.Add(new PresenceEventEntity
                    {
                        UserDiscordId = args.User.Id,
                        GuildDiscordId = guildId,

                        DesktopStatusBefore = GetStatusValue(args.PresenceBefore?.ClientStatus?.Desktop),
                        MobileStatusBefore = GetStatusValue(args.PresenceBefore?.ClientStatus?.Mobile),
                        WebStatusBefore = GetStatusValue(args.PresenceBefore?.ClientStatus?.Web),

                        DesktopStatusAfter = GetStatusValue(args.PresenceAfter?.ClientStatus?.Desktop),
                        MobileStatusAfter = GetStatusValue(args.PresenceAfter?.ClientStatus?.Mobile),
                        WebStatusAfter = GetStatusValue(args.PresenceAfter?.ClientStatus?.Web),

                        ActivitiesBeforeJson = SerializeActivities(args.PresenceBefore?.Activities),
                        ActivitiesAfterJson = SerializeActivities(args.PresenceAfter?.Activities),

                        EventTimestampUtc = ctx.ReceivedAtUtc,
                        ReceivedAtUtc = ctx.ReceivedAtUtc,
                        RawEventJson = ctx.RawJson
                    });

                    await ctx.Db.SaveChangesAsync();

                    // Track activities in ActivityEntity — upsert the user first so we never silently
                    // skip activity tracking for unknown users (87k presence events historically).
                    var userService = ctx.Services.GetRequiredService<UserService>();
                    var userResult = await userService.UpsertUserAsync(args.User);

                    if (userResult.IsSuccess)
                    {
                        await UpdateActivityTracking(ctx.Db, userResult.Value, args.PresenceBefore?.Activities, args.PresenceAfter?.Activities, ctx.ReceivedAtUtc);
                        await ctx.Db.SaveChangesAsync();
                    }
                    else
                    {
                        ctx.Logger.LogWarning("Skipping activity tracking: UpsertUserAsync did not produce a User row for {UserId}", args.User.Id);
                    }

                    await tx.CommitAsync();
                });

                ctx.Logger.LogDebug("Recorded presence event for user {UserId}", args.User.Id);
            });
    }

    private static async Task UpdateActivityTracking(
        DiscordDbContext dbContext,
        Guid userGuid,
        IReadOnlyList<DiscordActivity>? activitiesBefore,
        IReadOnlyList<DiscordActivity>? activitiesAfter,
        DateTime now)
    {
        var endedActivities = activitiesBefore?.Where(b =>
            !activitiesAfter?.Any(a => IsSameActivity(a, b)) ?? true) ?? [];

        var startedActivities = activitiesAfter?.Where(a =>
            !activitiesBefore?.Any(b => IsSameActivity(a, b)) ?? true) ?? [];

        foreach (var ended in endedActivities)
        {
            var existingActivity = await dbContext.Activities
                .Where(a => a.UserId == userGuid && a.IsActive && a.Name == ended.Name && a.ActivityType == (int)ended.ActivityType)
                .FirstOrDefaultAsync();

            if (existingActivity != null)
            {
                existingActivity.IsActive = false;
                existingActivity.EndedAtUtc = now;
                existingActivity.LastSeenAtUtc = now;
            }
        }

        foreach (var started in startedActivities)
        {
            var activity = new ActivityEntity
            {
                UserId = userGuid,
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

        var continuingActivities = (activitiesAfter ?? [])
            .Where(current => activitiesBefore?.Any(b => IsSameActivity(current, b)) == true);

        foreach (var current in continuingActivities)
        {
            var existingActivity = await dbContext.Activities
                .Where(a => a.UserId == userGuid && a.IsActive && a.Name == current.Name && a.ActivityType == (int)current.ActivityType)
                .FirstOrDefaultAsync();

            if (existingActivity is not null) existingActivity.LastSeenAtUtc = now;
        }
    }

    private static bool IsSameActivity(DiscordActivity a, DiscordActivity b) =>
        a.Name == b.Name && a.ActivityType == b.ActivityType;

    private static int GetStatusValue(Optional<DiscordUserStatus>? status) =>
        status is { HasValue: true } ? (int)status.Value.Value : (int)DiscordUserStatus.Offline;

    private static string? SerializeActivities(IReadOnlyList<DiscordActivity>? activities)
    {
        if (activities == null || activities.Count == 0)
            return null;

        var activityData = activities.Select(a => new
        {
            Name = a.Name,
            Type = (int)a.ActivityType,
            StreamUrl = a.StreamUrl
        }).ToList();

        return JsonSerializer.Serialize(activityData, JsonOptions);
    }
}
