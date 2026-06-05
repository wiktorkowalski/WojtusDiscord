namespace DiscordEventService.Dtos;

/// <summary>Offset-paginated result envelope shared by the explorer and entity endpoints.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    long TotalCount,
    int Page,
    int PageSize);
