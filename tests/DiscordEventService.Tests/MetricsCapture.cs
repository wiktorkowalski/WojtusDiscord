using System.Diagnostics.Metrics;
using DiscordEventService.Infrastructure;

namespace DiscordEventService.Tests;

// Records what the app meter measures while it is alive. The meter is static and the test
// classes run in parallel, so a capture sees the measurements of every test: an assertion
// must select by a tag value only its own test uses, never by a total.
internal sealed class MetricsCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<Measured> _measurements = [];

    public MetricsCapture()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == BotMetrics.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.Start();
    }

    // The measurements of one instrument that carry the given tag value.
    public List<Measured> Of(string instrument, string tagKey, object tagValue)
    {
        lock (_measurements)
        {
            return _measurements
                .Where(m => m.Instrument == instrument && Equals(m.Tags.GetValueOrDefault(tagKey), tagValue))
                .ToList();
        }
    }

    // Every measurement of one instrument. For an instrument with no tag of its own: the
    // assertion must then hold whatever another test measured at the same time.
    public List<Measured> Of(string instrument)
    {
        lock (_measurements)
            return _measurements.Where(m => m.Instrument == instrument).ToList();
    }

    // An observable gauge measures nothing by itself: this does what a scrape does.
    public void Observe() => _listener.RecordObservableInstruments();

    public void Dispose() => _listener.Dispose();

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>();
        foreach (var tag in tags)
            copy[tag.Key] = tag.Value;

        lock (_measurements)
            _measurements.Add(new Measured(instrument.Name, value, copy));
    }

    internal sealed record Measured(string Instrument, double Value, Dictionary<string, object?> Tags);
}
