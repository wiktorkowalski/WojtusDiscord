using DSharpPlus;

namespace DiscordEventService.Services;

internal sealed class HeartbeatBackgroundService(
    IServiceScopeFactory scopeFactory,
    DiscordClient discordClient,
    ILogger<HeartbeatBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    // #320: only a failed-write streak at least this long becomes a DbUnreachable row —
    // a single-tick flap is a transient the event-write retry layer already absorbs.
    private static readonly TimeSpan MinRecordableOutage = TimeSpan.FromSeconds(30);

    private readonly UnwritableWindowTracker _outage = new(MinRecordableOutage);

    // Only emit a Debug log on the first occurrence of each consecutive
    // failure streak — avoids per-tick log spam when DSharpPlus has been
    // disconnected for an extended period.
    private bool _connectedLookupFailed;
    private bool _latencyLookupFailed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            var nowUtc = DateTime.UtcNow;
            var (isConnected, latencyMs) = ReadGatewayState();
            var heartbeatWritten = false;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var tracker = scope.ServiceProvider.GetRequiredService<DowntimeTrackerService>();
                await tracker.RecordHeartbeatAsync(nowUtc, isConnected, latencyMs);
                heartbeatWritten = true;

                var hadPendingWindow = _outage.HasPendingWindow;
                var window = _outage.OnWriteSucceeded(nowUtc);
                if (window is not null)
                {
                    // The DB is writable again — persist the whole unwritable stretch as one
                    // closed DbUnreachable interval (#320). Reset only after the commit, so a
                    // failure here keeps the window pending and the next tick retries it.
                    await tracker.RecordDbUnreachableAsync(window);
                    _outage.Reset();
                }
                else if (hadPendingWindow)
                {
                    logger.LogDebug(
                        "Heartbeat writes recovered within {ThresholdSeconds}s — transient, no downtime row",
                        MinRecordableOutage.TotalSeconds);
                }
            }
            catch (Exception ex) when (!heartbeatWritten)
            {
                // Only a failure of the heartbeat write itself counts toward the window —
                // scope/persist failures on a tick whose heartbeat committed must not.
                _outage.OnWriteFailed(nowUtc, isConnected);
                logger.LogWarning(ex, "Heartbeat write failed; will retry next tick");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Recording the DbUnreachable interval failed; will retry next tick");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private (bool? IsConnected, int? LatencyMs) ReadGatewayState()
    {
        // DiscordClient.AllShardsConnected is the direct gateway-up signal; the
        // per-guild latency is informational. Both are wrapped in try/catch
        // because DSharpPlus can briefly throw during reconnect handshakes.
        bool? isConnected = null;
        int? latencyMs = null;

        try
        {
            isConnected = discordClient.AllShardsConnected;
            _connectedLookupFailed = false;
        }
        catch (Exception ex)
        {
            if (!_connectedLookupFailed)
            {
                logger.LogDebug(ex, "AllShardsConnected lookup failed (further failures suppressed until recovery)");
                _connectedLookupFailed = true;
            }
        }

        try
        {
            var firstGuildId = discordClient.Guilds.Keys.FirstOrDefault();
            if (firstGuildId != 0)
            {
                latencyMs = (int)discordClient.GetConnectionLatency(firstGuildId).TotalMilliseconds;
                _latencyLookupFailed = false;
            }
        }
        catch (Exception ex)
        {
            if (!_latencyLookupFailed)
            {
                logger.LogDebug(ex, "GetConnectionLatency lookup failed (further failures suppressed until recovery)");
                _latencyLookupFailed = true;
            }
        }

        return (isConnected, latencyMs);
    }
}
