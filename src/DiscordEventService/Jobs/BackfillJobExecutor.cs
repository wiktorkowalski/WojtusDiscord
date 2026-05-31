using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

/// <summary>
/// Owns the lifecycle every backfill job shares: a fresh DI scope + DbContext, checkpoint
/// get-or-create, the InProgress flip (with resume-cursor reset), and the four terminal
/// transitions — Completed, short-circuit→Failed, cancelled→Failed (no rethrow), faulted→Failed
/// (rethrow). Jobs supply only guild resolution + per-item work as a delegate, keeping
/// <c>DiscordClient</c> out of the executor so its transitions are unit-testable against a real
/// DbContext with no gateway.
/// </summary>
public sealed class BackfillJobExecutor(
    IServiceScopeFactory scopeFactory,
    ILogger<BackfillJobExecutor> logger)
{
    public async Task RunAsync(
        BackfillType type,
        ulong guildId,
        Func<BackfillContext, Task<BackfillOutcome>> work,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();

        var checkpoint = await GetOrCreateCheckpointAsync(db, guildId, type);

        // Only honor CurrentChannelId / LastProcessedId as a resume cursor when the previous run
        // was interrupted mid-flight (Status==InProgress). After a terminal status the cursor points
        // at the last channel processed; carrying it over would Skip all-but-the-last channel and
        // silently mask gap events. Cleared atomically with the InProgress flip so no crash window
        // leaves {InProgress, stale cursor}. No-op for the cursor-less jobs (fields stay null).
        if (checkpoint.Status != BackfillStatus.InProgress)
        {
            checkpoint.CurrentChannelId = null;
            checkpoint.LastProcessedId = null;
        }
        checkpoint.Status = BackfillStatus.InProgress;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var outcome = await work(new BackfillContext(db, scope.ServiceProvider, checkpoint));

            if (outcome.IsShortCircuit)
            {
                logger.LogWarning("{BackfillType} backfill short-circuited for guild {GuildId}: {Reason}",
                    type, guildId, outcome.Reason);
                await MarkFailedAsync(db, checkpoint, new InvalidOperationException(outcome.Reason));
                return;
            }

            await MarkCompletedAsync(db, checkpoint);
            logger.LogInformation("{BackfillType} backfill completed for guild {GuildId}: {Count} processed",
                type, guildId, checkpoint.ProcessedCount);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning("{BackfillType} backfill cancelled for guild {GuildId} (likely deploy restart)",
                type, guildId);
            await MarkFailedAsync(db, checkpoint, ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{BackfillType} backfill failed for guild {GuildId}", type, guildId);
            await MarkFailedAsync(db, checkpoint, ex);
            throw;
        }
    }

    private static async Task<BackfillCheckpointEntity> GetOrCreateCheckpointAsync(
        DiscordDbContext db, ulong guildId, BackfillType type)
    {
        var checkpoint = await db.BackfillCheckpoints
            .FirstOrDefaultAsync(c => c.GuildDiscordId == guildId && c.Type == type);

        if (checkpoint is null)
        {
            checkpoint = new BackfillCheckpointEntity
            {
                GuildDiscordId = guildId,
                Type = type,
                Status = BackfillStatus.Pending,
                StartedAtUtc = DateTime.UtcNow
            };
            db.BackfillCheckpoints.Add(checkpoint);
            // Terminal/lifecycle saves are arg-less so they still land if the run's token is
            // already cancelled (the cancelled path must persist a Failed status).
            await db.SaveChangesAsync();
        }

        return checkpoint;
    }

    private static async Task MarkCompletedAsync(DiscordDbContext db, BackfillCheckpointEntity checkpoint)
    {
        checkpoint.Status = BackfillStatus.Completed;
        checkpoint.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task MarkFailedAsync(DiscordDbContext db, BackfillCheckpointEntity checkpoint, Exception ex)
    {
        checkpoint.Status = BackfillStatus.Failed;
        checkpoint.ErrorCount++;
        checkpoint.LastError = $"{ex.GetType().Name}: {ex.Message}";
        checkpoint.LastErrorAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}

public sealed record BackfillContext(
    DiscordDbContext Db,
    IServiceProvider Services,
    BackfillCheckpointEntity Checkpoint);

public sealed record BackfillOutcome
{
    public required bool IsShortCircuit { get; init; }
    public string? Reason { get; init; }

    public static BackfillOutcome Completed { get; } = new BackfillOutcome { IsShortCircuit = false };

    public static BackfillOutcome ShortCircuit(string reason) =>
        new BackfillOutcome { IsShortCircuit = true, Reason = reason };
}
