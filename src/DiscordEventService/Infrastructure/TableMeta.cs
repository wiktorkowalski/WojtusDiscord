namespace DiscordEventService.Infrastructure;

public sealed class TableMeta
{
    private Dictionary<string, ColumnMeta>? _byName;

    public required string TableName { get; init; }
    public required string DisplayName { get; init; }
    public required string EntityName { get; init; }
    public required IReadOnlyList<ColumnMeta> Columns { get; init; }

    private Dictionary<string, ColumnMeta> ByName =>
        _byName ??= Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);

    public bool HasColumn(string column) => ByName.ContainsKey(column);

    public ColumnMeta? Column(string column) => ByName.GetValueOrDefault(column);

    // Stable default sort: the single-column PK if present, else the first column.
    public string DefaultSortColumn =>
        Columns.FirstOrDefault(c => c.IsPrimaryKey)?.Name ?? Columns[0].Name;
}
