using Hangfire;

namespace DiscordEventService.Jobs;

internal sealed class GuildBackfillOrchestrator(
    IBackgroundJobClient backgroundJobClient,
    ILogger<GuildBackfillOrchestrator> logger)
{
    public string StartBackfill(ulong guildId, BackfillOptions? options = null)
        => EnqueueChain(guildId, options ?? BackfillOptions.Default, afterTimestampUtc: null);

    // afterTimestampUtc stops Messages/Reactions scrolling once older than the window;
    // used by reconnect-driven and historical-gap-driven backfills.
    public string EnqueueBackfillFrom(ulong guildId, DateTime afterTimestampUtc, BackfillOptions? options = null)
        => EnqueueChain(guildId, options ?? BackfillOptions.Default, afterTimestampUtc);

    private string EnqueueChain(ulong guildId, BackfillOptions options, DateTime? afterTimestampUtc)
    {
        logger.LogInformation(
            "Starting backfill orchestration for guild {GuildId} with options {@Options}, backfilling after {AfterTimestampUtc:O}",
            guildId, options, afterTimestampUtc);

        var rolesJobId = backgroundJobClient.Enqueue<RolesBackfillJob>(
            j => j.ExecuteAsync(guildId, CancellationToken.None));

        var emojisJobId = backgroundJobClient.ContinueJobWith<EmojisBackfillJob>(
            rolesJobId, j => j.ExecuteAsync(guildId, CancellationToken.None));

        var stickersJobId = backgroundJobClient.ContinueJobWith<StickersBackfillJob>(
            emojisJobId, j => j.ExecuteAsync(guildId, CancellationToken.None));

        var channelsJobId = backgroundJobClient.ContinueJobWith<ChannelsBackfillJob>(
            stickersJobId, j => j.ExecuteAsync(guildId, CancellationToken.None));

        var membersJobId = backgroundJobClient.ContinueJobWith<MembersBackfillJob>(
            channelsJobId, j => j.ExecuteAsync(guildId, CancellationToken.None));

        var finalJobId = membersJobId;

        if (options.IncludeMessages)
        {
            var messagesJobId = backgroundJobClient.ContinueJobWith<MessagesBackfillJob>(
                membersJobId, j => j.ExecuteAsync(guildId, afterTimestampUtc, CancellationToken.None));
            finalJobId = messagesJobId;

            if (options.IncludeReactions)
            {
                var reactionsJobId = backgroundJobClient.ContinueJobWith<ReactionsBackfillJob>(
                    messagesJobId, j => j.ExecuteAsync(guildId, afterTimestampUtc, CancellationToken.None));
                finalJobId = reactionsJobId;
            }
        }

        logger.LogInformation("Backfill orchestration started for guild {GuildId}, first job: {JobId}, final job: {FinalJobId}",
            guildId, rolesJobId, finalJobId);

        return rolesJobId;
    }
}

internal sealed record BackfillOptions
{
    public bool IncludeMessages { get; init; } = true;
    public bool IncludeReactions { get; init; } = true;

    public static BackfillOptions Default => new BackfillOptions();
}
