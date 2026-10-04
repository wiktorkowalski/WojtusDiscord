using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;

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
        }
        catch (Exception ex)
        {
            // Warning, not Error: the search itself answered. The query is here because the
            // row that would hold it is lost.
            logger.LogWarning(
                ex,
                "Meme search log row lost: query {Query} by {UserId} in guild {GuildId}",
                search.Query, search.UserDiscordId, search.GuildDiscordId);
        }
    }
}
