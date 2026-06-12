namespace DiscordEventService.Services;

// Replaces the prior Guid.Empty-as-error sentinel so callers branch on IsSuccess, not a magic value.
internal sealed record UpsertResult<T>(bool IsSuccess, T? Value, string? FailureReason)
{
    public static UpsertResult<T> Success(T value) => new(true, value, null);

    public static UpsertResult<T> Failure(string reason) => new(false, default, reason);
}
