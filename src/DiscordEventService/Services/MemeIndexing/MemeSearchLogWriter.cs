using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Infrastructure;

namespace DiscordEventService.Services.MemeIndexing;

// Writes the meme search log (#384) off the answer path. The search hands over a finished row
// and returns; the insert runs on the thread pool with its own scope and DbContext, because the
// caller's DbContext is gone by then. One attempt: a failed write costs that one row.
public sealed class MemeSearchLogWriter(IServiceScopeFactory scopeFactory, ILogger<MemeSearchLogWriter> logger)
{
    // The write this instance started last. Production code never awaits it; tests do.
    internal Task LastWrite { get; private set; } = Task.CompletedTask;

    public void Write(MemeSearchLogEntity search) =>
        LastWrite = Task.Run(() => SaveAsync(search));

    private async Task SaveAsync(MemeSearchLogEntity search)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            db.MemeSearchLog.Add(search);

            // Not the caller's token: a conversation turn can cancel it right after the tool
            // returns. The command timeout bounds the write.
            await db.SaveChangesAsync(CancellationToken.None);
            BotMetrics.MemeSearchLogWritten(succeeded: true);
        }
        catch (Exception ex)
        {
            BotMetrics.MemeSearchLogWritten(succeeded: false);

            // Warning, not Error: the search itself answered. No query text and no user id: what
            // a person typed stays out of the application log, also when its row is lost.
            logger.LogWarning(
                ex,
                "Meme search log row lost: source {Source}, {ResultCount} hits",
                search.Source, search.ResultCount);
        }
    }
}
