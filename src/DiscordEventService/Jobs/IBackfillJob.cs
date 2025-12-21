namespace DiscordEventService.Jobs;

public interface IBackfillJob
{
    Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken);
}
