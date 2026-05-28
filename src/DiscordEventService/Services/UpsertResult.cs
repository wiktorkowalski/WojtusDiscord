namespace DiscordEventService.Services;

/// <summary>
/// Outcome of an upsert: success carries the resolved <typeparamref name="T"/>; failure carries a
/// reason (already logged by the service). Replaces the prior <c>Guid.Empty</c>-as-error sentinel so
/// callers branch on <see cref="IsSuccess"/> instead of pattern-matching a magic value.
/// </summary>
public sealed record UpsertResult<T>(bool IsSuccess, T? Value, string? FailureReason)
{
    public static UpsertResult<T> Success(T value) => new(true, value, null);

    public static UpsertResult<T> Failure(string reason) => new(false, default, reason);
}
