namespace DiscordEventService.Jobs;

internal interface IBackfillJob
{
    Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken);
}
