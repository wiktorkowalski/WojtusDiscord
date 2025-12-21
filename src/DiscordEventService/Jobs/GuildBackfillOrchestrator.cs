using Hangfire;

namespace DiscordEventService.Jobs;

public class GuildBackfillOrchestrator(
    IBackgroundJobClient backgroundJobClient,
    ILogger<GuildBackfillOrchestrator> logger)
{
    public string StartBackfill(ulong guildId, BackfillOptions? options = null)
    {
        options ??= BackfillOptions.Default;

        logger.LogInformation("Starting backfill orchestration for guild {GuildId} with options: {@Options}", guildId, options);

        // Chain jobs in order using Hangfire continuations
        // Each job depends on the previous one completing
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

        string finalJobId = membersJobId;

        if (options.IncludeMessages)
        {
            var messagesJobId = backgroundJobClient.ContinueJobWith<MessagesBackfillJob>(
                membersJobId, j => j.ExecuteAsync(guildId, CancellationToken.None));
            finalJobId = messagesJobId;

            if (options.IncludeReactions)
            {
                var reactionsJobId = backgroundJobClient.ContinueJobWith<ReactionsBackfillJob>(
                    messagesJobId, j => j.ExecuteAsync(guildId, CancellationToken.None));
                finalJobId = reactionsJobId;
            }
        }

        logger.LogInformation("Backfill orchestration started for guild {GuildId}, first job: {JobId}, final job: {FinalJobId}",
            guildId, rolesJobId, finalJobId);

        return rolesJobId;
    }
}

public record BackfillOptions
{
    public bool IncludeMessages { get; init; } = true;
    public bool IncludeReactions { get; init; } = true;

    public static BackfillOptions Default => new();
}
