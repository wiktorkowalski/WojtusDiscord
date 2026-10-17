using DiscordEventService.Configuration;
using DiscordEventService.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DiscordEventService.Services.Conversation;

// Copies Conversation:QueryDatabase into the tables the query role's row-level security reads (#460,
// QuerySandboxVisibility migration). Runs at boot as the app login, which owns the tables; the query
// role has no privilege on them and its transaction is read only, so the model cannot change them.
// A GUC would not do: any SELECT can call set_config. Until this runs, the settings row keeps its
// default: deleted messages hidden, no channel hidden.
internal static class QuerySandboxSettingsWriter
{
    public static async Task ApplyAsync(
        DiscordDbContext db, QueryDatabaseOptions options, ILogger logger, CancellationToken cancellationToken)
    {
        // Snowflakes are stored as bigint; the cast keeps the bits, as EF's ulong mapping does.
        var hidden = options.HiddenChannelIds.Distinct().Select(id => unchecked((long)id)).ToArray();
        try
        {
            // One statement, so it is atomic without a transaction (the retrying execution strategy
            // refuses user transactions) and idempotent when retried. The DELETE and the INSERT touch
            // disjoint rows, so they can share the statement's snapshot.
            await db.Database.ExecuteSqlAsync($"""
                WITH settings AS (
                    INSERT INTO query_sandbox_settings (id, exclude_deleted_messages, updated_at_utc)
                    VALUES (1, {options.ExcludeDeletedMessages}, now())
                    ON CONFLICT (id) DO UPDATE
                    SET exclude_deleted_messages = EXCLUDED.exclude_deleted_messages, updated_at_utc = now()
                ), removed AS (
                    DELETE FROM query_sandbox_hidden_channels WHERE channel_discord_id <> ALL ({hidden})
                )
                INSERT INTO query_sandbox_hidden_channels (channel_discord_id)
                SELECT unnest({hidden}) ON CONFLICT DO NOTHING
                """, cancellationToken);
            logger.LogInformation(
                "query_database visibility: exclude deleted messages {ExcludeDeleted}, {HiddenCount} hidden channel ids",
                options.ExcludeDeletedMessages, hidden.Length);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // AutoMigrate off and the migration not applied yet: nothing to write to.
            logger.LogWarning("query_database visibility settings not written, the tables do not exist yet");
        }
    }
}
