using System.Text.Json;
using DiscordEventService.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace DiscordEventService.Tests;

// The #193 /health payload: commit parsing out of InformationalVersion and the JSON shape
// the endpoint serves. The sha ride-along is "1.0.0+<sha>" — the SDK appends
// SourceRevisionId after '+'; local builds have neither sha nor timestamp.
public sealed class BuildInfoTests
{
    [Fact]
    public void ADockerBuild_YieldsTheFullAndShortCommit()
    {
        var info = BuildInfo.Parse("1.0.0+f82efc8badc0ffee00d1e5c0ffeec0de5eed1234", "2026-08-16T12:00:00Z");

        Assert.Equal("f82efc8badc0ffee00d1e5c0ffeec0de5eed1234", info.Commit);
        Assert.Equal("f82efc8", info.CommitShort);
        Assert.Equal("2026-08-16T12:00:00Z", info.BuildTimestampUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+")]
    public void ALocalBuild_DegradesToUnknown_InsteadOfCrashing(string? informationalVersion)
    {
        var info = BuildInfo.Parse(informationalVersion, null);

        Assert.Equal("unknown", info.Commit);
        Assert.Equal("unknown", info.CommitShort);
        Assert.Equal("unknown", info.BuildTimestampUtc);
    }

    [Fact]
    public void TheHealthPayload_CarriesStatusChecksVersionRuntimeAndDiscord()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["DiscordDbContext"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null),
            },
            TimeSpan.Zero);
        var build = BuildInfo.Parse("1.0.0+abcdef1234", "2026-08-16T12:00:00Z");
        var started = new DateTime(2026, 8, 16, 10, 0, 0, DateTimeKind.Utc);

        var json = JsonSerializer.Serialize(HealthResponseWriter.BuildPayload(
            report, build, "Production", started, started.AddHours(2), gatewayConnected: true, gatewayLatencyMs: 42));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.Equal("Healthy", root.GetProperty("checks").GetProperty("DiscordDbContext").GetString());
        Assert.Equal("abcdef1234", root.GetProperty("version").GetProperty("commit").GetString());
        Assert.Equal("abcdef1", root.GetProperty("version").GetProperty("commitShort").GetString());
        Assert.Equal("Production", root.GetProperty("runtime").GetProperty("environment").GetString());
        Assert.Equal("0.02:00:00", root.GetProperty("runtime").GetProperty("uptime").GetString());
        Assert.True(root.GetProperty("discord").GetProperty("connected").GetBoolean());
        Assert.Equal(42, root.GetProperty("discord").GetProperty("gatewayLatencyMs").GetInt32());
    }
}
