using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DiscordEventService.Infrastructure;

/// <summary>
/// Logical type of a column, derived from EF model metadata. Drives both how the
/// generic explorer coerces raw SQL values and how the frontend renders each cell.
/// </summary>
public enum ColumnKind
{
    String,
    Int,
    Long,
    Snowflake,
    Bool,
    Timestamp,
    Uuid,
    Json,
    Number,
    Enum,
    Other,
}

public sealed record ColumnMeta(
    string Name,
    ColumnKind Kind,
    bool IsPrimaryKey,
    bool IsNullable,
    IReadOnlyList<EnumValue>? EnumValues);

public sealed record EnumValue(int Value, string Name);

public sealed class TableMeta
{
    public required string TableName { get; init; }
    public required string DisplayName { get; init; }
    public required string EntityName { get; init; }
    public required IReadOnlyList<ColumnMeta> Columns { get; init; }

    private Dictionary<string, ColumnMeta>? _byName;
    private Dictionary<string, ColumnMeta> ByName =>
        _byName ??= Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);

    public bool HasColumn(string column) => ByName.ContainsKey(column);

    public ColumnMeta? Column(string column) => ByName.GetValueOrDefault(column);

    /// <summary>Stable default sort: the single-column PK if present, else the first column.</summary>
    public string DefaultSortColumn =>
        Columns.FirstOrDefault(c => c.IsPrimaryKey)?.Name ?? Columns[0].Name;
}

/// <summary>
/// Startup-cached whitelist of explorable tables and their columns, built from the
/// EF Core model. This is the security boundary for the generic explorer: only table
/// and column identifiers present here are ever emitted into SQL, and the emitted
/// literal is the trusted model value — never client-supplied text.
/// <para>
/// Hangfire tables are not EF entities (Hangfire uses its own storage) so they never
/// appear here; <c>__EFMigrationsHistory</c> is not an EF entity either, but is filtered
/// defensively.
/// </para>
/// </summary>
public sealed class SchemaCatalog
{
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
            {
                continue;
            }

            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName) || tableName == "__EFMigrationsHistory")
            {
                continue;
            }

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
                {
                    continue;
                }

                var kind = DetermineKind(property);
                columns.Add(new ColumnMeta(
                    columnName,
                    kind,
                    pkColumns.Contains(columnName),
                    property.IsNullable,
                    kind == ColumnKind.Enum ? EnumValuesFor(property.ClrType) : null));
            }

            if (columns.Count == 0)
            {
                continue;
            }

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
        {
            return ColumnKind.Json;
        }

        if (clrType == typeof(string)) return ColumnKind.String;
        if (clrType == typeof(int) || clrType == typeof(short) || clrType == typeof(byte)
            || clrType == typeof(sbyte) || clrType == typeof(uint) || clrType == typeof(ushort))
        {
            return ColumnKind.Int;
        }
        if (clrType == typeof(long)) return ColumnKind.Long;
        if (clrType == typeof(decimal) || clrType == typeof(double) || clrType == typeof(float))
        {
            return ColumnKind.Number;
        }

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
