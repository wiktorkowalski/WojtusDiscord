namespace DiscordEventService.Services;

// The in-memory state machine behind #320: heartbeat writes are the DB-liveness probe
// (they tick every 5s regardless of Discord activity), so a streak of failed writes IS
// the unwritable window. In-memory on purpose — during the outage the DB cannot hold
// state, so the window is buffered here and persisted only after the first successful
// write. A process restart mid-outage loses it; the startup gap inference then records
// the tail as Inferred.
internal sealed class UnwritableWindowTracker(TimeSpan minRecordableWindow)
{
    private DateTime? _sinceUtc;
    private DateTime? _recoveredAtUtc;
    private int _failedWrites;

    public bool HasPendingWindow => _sinceUtc is not null;

    public void OnWriteFailed(DateTime nowUtc, bool? gatewayConnected)
    {
        if (_sinceUtc is not null)
        {
            // Once open, the window is owned by this signal even if the gateway drops
            // mid-outage — the whole unwritable stretch stays one interval. A new failure
            // while a recovered window still awaits its persist re-opens it: the merged
            // interval over-reports the writable gap between the two outages, but the
            // alternative is losing the first outage entirely.
            _recoveredAtUtc = null;
            _failedWrites++;
            return;
        }

        // Only a failure with the gateway still up opens a window: with the gateway down
        // (or unknown) the process is in a full outage the gateway-disconnect and
        // startup-gap paths already own.
        if (gatewayConnected != true)
            return;

        _sinceUtc = nowUtc;
        _failedWrites = 1;
    }

    // The window to persist, or null when there is nothing recordable — a streak shorter
    // than minRecordableWindow is a transient (the event-write layer already retries
    // those) and is discarded here.
    public UnwritableWindow? OnWriteSucceeded(DateTime nowUtc)
    {
        if (_sinceUtc is null)
            return null;

        // Pin the recovery instant to the FIRST successful write. A retry after a failed
        // persist must not slide EndedAtUtc forward — the DB was writable for that
        // stretch, and the successful heartbeat rows in it would contradict the interval.
        _recoveredAtUtc ??= nowUtc;

        if (_recoveredAtUtc.Value - _sinceUtc.Value < minRecordableWindow)
        {
            Reset();
            return null;
        }

        return new UnwritableWindow(_sinceUtc.Value, _recoveredAtUtc.Value, _failedWrites);
    }

    // Deliberately NOT folded into OnWriteSucceeded for a recordable window: the caller
    // resets only after the interval row is committed, so a failed persist keeps the
    // window pending and the next successful write retries it.
    public void Reset()
    {
        _sinceUtc = null;
        _recoveredAtUtc = null;
        _failedWrites = 0;
    }
}

internal sealed record UnwritableWindow(DateTime StartedAtUtc, DateTime EndedAtUtc, int FailedWriteCount);
