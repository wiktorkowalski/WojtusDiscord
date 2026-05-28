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
        Expression<Func<TEntity, TResult>> select)
        where TEntity : class
    {
        var rowsAffected = await set.Where(match).ExecuteUpdateAsync(update);
        if (rowsAffected == 0)
        {
            // No public API exposes the DbContext from a DbSet; this is the standard EF accessor.
            var db = set.GetService<ICurrentDbContext>().Context;
            try
            {
                set.Add(create());
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
            {
                // Race: another writer inserted first — drop the failed Add and update instead.
                db.ChangeTracker.Clear();
                await set.Where(match).ExecuteUpdateAsync(update);
            }
        }
        return await set.Where(match).Select(select).FirstOrDefaultAsync();
    }
}
