using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Npgsql;

namespace DiscordEventService.Data;

internal static class DbSetUpsertExtensions
{
    public static async Task<TResult?> UpsertAsync<TEntity, TResult>(
        this DbSet<TEntity> set,
        Expression<Func<TEntity, bool>> match,
        Action<UpdateSettersBuilder<TEntity>> update,
        Func<TEntity> create,
        Expression<Func<TEntity, TResult>> select,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        var rowsAffected = await set.Where(match).ExecuteUpdateAsync(update, cancellationToken);
        if (rowsAffected == 0)
        {
            // No public API exposes the DbContext from a DbSet; this is the standard EF accessor.
            var db = set.GetService<ICurrentDbContext>().Context;
            try
            {
                set.Add(create());
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
            {
                // Race: another writer inserted first — drop the failed Add and update instead.
                db.ChangeTracker.Clear();
                await set.Where(match).ExecuteUpdateAsync(update, cancellationToken);
            }
        }
        return await set.Where(match).Select(select).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Insert-or-get. Inserts <paramref name="create"/>; on a 23505 unique-violation race
    /// (another writer inserted first), clears the failed Add and re-queries the existing row
    /// <b>without modifying it</b>. Use when the conflicting row must be preserved as-is
    /// (placeholder rows, snapshot insert-or-ignore, create-only message inserts) — routing
    /// those through <see cref="UpsertAsync{TEntity,TResult}"/> would overwrite real data.
    /// Returns <c>(Entity, Inserted)</c>: on the happy path the freshly-inserted entity (keys
    /// populated, no extra query) with <c>Inserted = true</c>; on conflict the existing row with
    /// <c>Inserted = false</c> (<c>Entity = null</c> only if the row vanished after the conflict).
    /// Transaction-passive: never opens its own strategy/transaction.
    /// </summary>
    public static async Task<(TEntity? Entity, bool Inserted)> GetOrInsertAsync<TEntity>(
        this DbSet<TEntity> set,
        Expression<Func<TEntity, bool>> match,
        Func<TEntity> create,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        // No public API exposes the DbContext from a DbSet; this is the standard EF accessor.
        var db = set.GetService<ICurrentDbContext>().Context;
        var entity = create();
        try
        {
            set.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            return (entity, true);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Race: another writer inserted first — drop the failed Add and return the existing
            // row untouched (deliberately NO update; the existing data may be richer than ours).
            db.ChangeTracker.Clear();
            return (await set.Where(match).FirstOrDefaultAsync(cancellationToken), false);
        }
    }
}
