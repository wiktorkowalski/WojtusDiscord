using System.Reflection;

namespace DiscordEventService.Infrastructure;

// What /health reports about the running binary (#193). Resolved once at registration —
// assembly attributes never change at runtime. The commit rides in InformationalVersion
// as "1.0.0+<sha>" (the SDK appends SourceRevisionId, passed by the Docker build); the
// timestamp is the BuildTimestampUtc assembly metadata the csproj bakes alongside it.
// Local builds carry neither and degrade to "unknown" instead of crashing.
internal sealed record BuildInfo(
    string Commit, string CommitShort, string InformationalVersion, string BuildTimestampUtc)
{
    private const int ShortShaLength = 7;
    private const string Unknown = "unknown";

    public static BuildInfo FromEntryAssembly()
    {
        var assembly = Assembly.GetEntryAssembly();
        return Parse(
            assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => string.Equals(a.Key, "BuildTimestampUtc", StringComparison.Ordinal))?.Value);
    }

    internal static BuildInfo Parse(string? informationalVersion, string? buildTimestampUtc)
    {
        var informational = string.IsNullOrWhiteSpace(informationalVersion) ? Unknown : informationalVersion;
        var plus = informational.IndexOf('+');
        var commit = plus >= 0 && plus < informational.Length - 1 ? informational[(plus + 1)..] : Unknown;
        var commitShort = commit.Length > ShortShaLength ? commit[..ShortShaLength] : commit;
        var timestamp = string.IsNullOrWhiteSpace(buildTimestampUtc) ? Unknown : buildTimestampUtc;
        return new BuildInfo(commit, commitShort, informational, timestamp);
    }
}
