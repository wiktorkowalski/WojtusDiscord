using DiscordEventService.Infrastructure;

namespace DiscordEventService.Dtos;

/// <summary>One explorable table in the generic schema explorer.</summary>
public sealed record TableInfoDto(
    string Name,
    string DisplayName,
    string EntityName,
    long RowCount,
    bool Populated);

/// <summary>Column metadata that drives client-side rendering of explorer rows.</summary>
public sealed record ColumnMetadataDto(
    string Name,
    string Kind,
    bool IsPrimaryKey,
    bool IsNullable,
    IReadOnlyList<EnumValueDto>? EnumValues)
{
    public static ColumnMetadataDto From(ColumnMeta meta) => new(
        meta.Name,
        // camelCase the enum kind so it lands as e.g. "snowflake", "timestamp" in JSON.
        char.ToLowerInvariant(meta.Kind.ToString()[0]) + meta.Kind.ToString()[1..],
        meta.IsPrimaryKey,
        meta.IsNullable,
        meta.EnumValues?.Select(e => new EnumValueDto(e.Value, e.Name)).ToList());
}

public sealed record EnumValueDto(int Value, string Name);
