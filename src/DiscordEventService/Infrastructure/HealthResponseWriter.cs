using System.Diagnostics;
using System.Text.Json;
using DiscordEventService.Services.Conversation;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DiscordEventService.Infrastructure;

// The /health payload (#193): "what's running and is it healthy" from one endpoint.
// Everything added here is in-memory state — the only I/O in a /health request stays the
// EF liveness check the pipeline already ran, so the 30s Docker HEALTHCHECK stays cheap.
internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var build = context.RequestServices.GetRequiredService<BuildInfo>();
        var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
        var (connected, latencyMs) = ReadGatewayState(context.RequestServices.GetRequiredService<DiscordClientAccessor>());

        var payload = BuildPayload(
            report, build, environment.EnvironmentName,
            Process.GetCurrentProcess().StartTime.ToUniversalTime(), DateTime.UtcNow,
            connected, latencyMs);

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, Json));
    }

    internal static object BuildPayload(
        HealthReport report, BuildInfo build, string environmentName,
        DateTime startedAtUtc, DateTime nowUtc, bool? gatewayConnected, int? gatewayLatencyMs) => new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString(), StringComparer.Ordinal),
            version = new
            {
                commit = build.Commit,
                commitShort = build.CommitShort,
                informationalVersion = build.InformationalVersion,
                buildTimestampUtc = build.BuildTimestampUtc,
            },
            runtime = new
            {
                environment = environmentName,
                startedAtUtc,
                uptime = (nowUtc - startedAtUtc).ToString(@"d\.hh\:mm\:ss"),
            },
            discord = new
            {
                connected = gatewayConnected,
                gatewayLatencyMs,
            },
        };

    private static (bool? Connected, int? LatencyMs) ReadGatewayState(DiscordClientAccessor accessor)
    {
        // The accessor throws until Program.cs sets it just before app.Run — a probe that
        // races boot (Docker HEALTHCHECK starts at 5s) reports connected=null, not a 500.
        bool? connected = null;
        int? latencyMs = null;
        try
        {
            var client = accessor.Client;
            connected = client.AllShardsConnected;

            // Separate guard: DSharpPlus can throw here mid-reconnect-handshake while
            // AllShardsConnected already answered — keep the connected flag we have.
            var firstGuildId = client.Guilds.Keys.FirstOrDefault();
            if (firstGuildId != 0)
                latencyMs = (int)client.GetConnectionLatency(firstGuildId).TotalMilliseconds;
        }
        catch (Exception)
        {
            // Informational fields only — never fail the health response over them.
        }

        return (connected, latencyMs);
    }
}
