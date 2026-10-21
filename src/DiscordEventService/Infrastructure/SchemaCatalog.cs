using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DiscordEventService.Infrastructure;

public sealed record ColumnMeta(
    string Name,
    ColumnKind Kind,
    bool IsPrimaryKey,
    bool IsNullable,
    IReadOnlyList<EnumValue>? EnumValues);

public sealed record EnumValue(int Value, string Name);

// Security boundary for the generic explorer: only table/column identifiers present here are ever emitted
// into SQL, and the emitted literal is the trusted model value — never client-supplied text. Hangfire and
// __EFMigrationsHistory are not EF entities so they never appear here.
public sealed class SchemaCatalog
{
    // #388: an EF table is in the catalog, so in the explorer and in the assistant's schema hint, only
    // when it is named here. A new table stays out until someone opens it on purpose; a test fails while
    // an EF table is in neither list. The query role's SELECT follows the same decision: since
    // RevokeDefaultSelectFromQueryRole a new table gets no grant unless its migration adds one.
    internal static readonly HashSet<string> VisibleTables =
    [
        "activities", "audit_log_events", "auto_mod_events", "auto_mod_rule_events", "auto_mod_rules",
        "backfill_checkpoints", "ban_events", "bans", "bot_downtime_intervals", "bot_heartbeats",
        "channel_events", "channels", "emoji_events", "emotes", "failed_events", "guild_events",
        "guild_members_chunk_events", "guild_scheduled_events", "guilds", "integration_events", "integrations",
        "invite_events", "invites", "member_events", "member_role_snapshots", "members", "meme_annotations",
        "meme_index", "message_edit_history", "message_events", "message_mentions", "messages", "pin_events",
        "poll_events", "presence_events", "raw_event_logs", "reaction_events", "role_events", "roles",
        "scheduled_events", "stage_instance_events", "stage_instances", "sticker_events", "stickers",
        "thread_events", "thread_sync_events", "typing_events", "user_name_history", "users",
        "voice_server_events", "voice_state_events", "webhook_events", "webhooks",
    ];

    // Closed on purpose: these tables hold what each person typed. The meme search log (#384) and the
    // assistant's conversations, DMs included, with their usage ledger and alerts (#444). The
    // AddMemeSearchLog and RevokeConversationTablesFromQueryRole migrations revoke the query role's SELECT.
    internal static readonly HashSet<string> HiddenTables =
    [
        "meme_search_log", "meme_search_log_results",
        "conversation", "conversation_message", "conversation_usage", "usage_alerts",
    ];

    private readonly Dictionary<string, TableMeta> _tables;

    private SchemaCatalog(Dictionary<string, TableMeta> tables) => _tables = tables;

    public IReadOnlyCollection<TableMeta> Tables => _tables.Values;

    public bool TryGetTable(string tableName, out TableMeta table) =>
        _tables.TryGetValue(tableName, out table!);

    public static SchemaCatalog Build(IModel model)
    {
        var tables = new Dictionary<string, TableMeta>(StringComparer.Ordinal);

        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.IsOwned())
                continue;

            var tableName = entityType.GetTableName();
            if (tableName is null || !VisibleTables.Contains(tableName))
                continue;

            var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            var pkColumns = entityType.FindPrimaryKey()?.Properties
                .Select(p => p.GetColumnName(storeObject))
                .Where(n => n is not null)
                .ToHashSet(StringComparer.Ordinal) ?? [];

            var columns = new List<ColumnMeta>();
            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                if (columnName is null)
                    continue;

                var kind = DetermineKind(property);
                columns.Add(new ColumnMeta(
                    columnName,
                    kind,
                    pkColumns.Contains(columnName),
                    property.IsNullable,
                    kind == ColumnKind.Enum ? EnumValuesFor(property.ClrType) : null));
            }

            if (columns.Count == 0)
                continue;

            tables[tableName] = new TableMeta
            {
                TableName = tableName,
                DisplayName = Humanize(tableName),
                EntityName = entityType.ClrType.Name,
                Columns = columns,
            };
        }

        return new SchemaCatalog(tables);
    }

    private static ColumnKind DetermineKind(IProperty property)
    {
        var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (clrType.IsEnum) return ColumnKind.Enum;
        if (clrType == typeof(ulong)) return ColumnKind.Snowflake;
        if (clrType == typeof(Guid)) return ColumnKind.Uuid;
        if (clrType == typeof(DateTime) || clrType == typeof(DateTimeOffset)) return ColumnKind.Timestamp;
        if (clrType == typeof(bool)) return ColumnKind.Bool;

        var columnType = property.GetColumnType();
        if (columnType is not null && columnType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return ColumnKind.Json;

        if (clrType == typeof(string)) return ColumnKind.String;
        if (clrType == typeof(int) || clrType == typeof(short) || clrType == typeof(byte)
            || clrType == typeof(sbyte) || clrType == typeof(uint) || clrType == typeof(ushort))
            return ColumnKind.Int;
        if (clrType == typeof(long)) return ColumnKind.Long;
        if (clrType == typeof(decimal) || clrType == typeof(double) || clrType == typeof(float))
            return ColumnKind.Number;

        return ColumnKind.Other;
    }

    private static IReadOnlyList<EnumValue> EnumValuesFor(Type clrType)
    {
        var enumType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        return Enum.GetValues(enumType)
            .Cast<object>()
            .Select(v => new EnumValue(Convert.ToInt32(v), v.ToString() ?? string.Empty))
            .ToList();
    }

    private static string Humanize(string snakeCase) =>
        string.Join(' ', snakeCase
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));
}
