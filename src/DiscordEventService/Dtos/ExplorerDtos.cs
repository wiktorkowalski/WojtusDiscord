using DiscordEventService.Infrastructure;

namespace DiscordEventService.Dtos;

public sealed record TableInfoDto(
    string Name,
    string DisplayName,
    string EntityName,
    long RowCount,
    bool Populated);

public sealed record ColumnMetadataDto(
    string Name,
    string Kind,
    bool IsPrimaryKey,
    bool IsNullable,
    IReadOnlyList<EnumValueDto>? EnumValues)
{
    public static ColumnMetadataDto From(ColumnMeta meta) => new ColumnMetadataDto(
        meta.Name,
        // camelCase the enum kind so it lands as e.g. "snowflake", "timestamp" in JSON.
        char.ToLowerInvariant(meta.Kind.ToString()[0]) + meta.Kind.ToString()[1..],
        meta.IsPrimaryKey,
        meta.IsNullable,
        meta.EnumValues?.Select(e => new EnumValueDto(e.Value, e.Name)).ToList());
}

public sealed record EnumValueDto(int Value, string Name);
