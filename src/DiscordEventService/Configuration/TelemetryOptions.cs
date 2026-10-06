namespace DiscordEventService.Configuration;

internal sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    private const string TracesPath = "/v1/traces";

    // The BASE URL of an OTLP/HTTP receiver (Grafana Tempo), for example "http://tempo:4318".
    // The code appends "/v1/traces". Empty = no trace export to Tempo.
    public string? OtlpTracesEndpoint { get; set; }

    public bool TracesConfigured => !string.IsNullOrWhiteSpace(OtlpTracesEndpoint);

    public bool HasValidTracesEndpoint =>
        !TracesConfigured
        || (Uri.TryCreate(OtlpTracesEndpoint!.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    // A value that already ends in /v1/traces is taken as it is, so both spellings work.
    public Uri TracesUri
    {
        get
        {
            var endpoint = OtlpTracesEndpoint!.Trim().TrimEnd('/');
            return new Uri(endpoint.EndsWith(TracesPath, StringComparison.OrdinalIgnoreCase) ? endpoint : endpoint + TracesPath);
        }
    }
}
