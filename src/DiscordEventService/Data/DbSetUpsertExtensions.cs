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

    // Insert-or-get: on a 23505 race the existing row is returned UNMODIFIED (unlike UpsertAsync,
    // which would overwrite it) — for rows that must be preserved as-is. Returns (Entity, Inserted):
    // Inserted=true with the fresh entity on the happy path; Inserted=false with the existing row on
    // conflict (Entity=null only if the row vanished after the conflict).
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
