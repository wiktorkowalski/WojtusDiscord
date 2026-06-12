using DiscordEventService.Data;

namespace DiscordEventService.Services.Pipeline;

internal sealed record EventContext(
    DiscordDbContext Db,
    IServiceProvider Services,
    Guid CorrelationId,
    string? RawJson,
    DateTime ReceivedAtUtc,
    ILogger Logger,
    Func<Exception, Task> RecordFailureAsync);
