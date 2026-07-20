using System.Text.RegularExpressions;
using Xunit;

namespace DiscordEventService.Tests;

// Architecture guard: the raw upsert primitives (UpsertAsync/GetOrInsertAsync) on seam-owned
// DbSets may only be called from the service that owns that entity's column map. Anything else
// re-inlines a column map the seam exists to centralize — the drift these seams were built to
// prevent (#290, #304, #306).
public sealed class UpsertSeamArchitectureTests
{
    // DbSet name → the single file allowed to call upsert primitives on it.
    private static readonly IReadOnlyDictionary<string, string> SeamOwners = new Dictionary<string, string>
    {
        ["Guilds"] = "GuildUpsertService.cs",
        ["Channels"] = "ChannelUpsertService.cs",
        ["Roles"] = "RoleUpsertService.cs",
        ["Emotes"] = "EmoteUpsertService.cs",
        ["Stickers"] = "StickerUpsertService.cs",
        ["Users"] = "UserService.cs",
        ["Members"] = "UserService.cs",
    };

    private static readonly Regex SeamOwnedUpsertCall = new(
        @"\.(?<set>Guilds|Channels|Roles|Emotes|Stickers|Users|Members)\s*\.\s*(?:UpsertAsync|GetOrInsertAsync)\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void SeamOwnedUpserts_AreOnlyCalledFromTheirOwningService()
    {
        List<string> violations = [];

        foreach (var (file, text) in EnumerateSourceFiles())
        {
            var fileName = Path.GetFileName(file);
            // Explicit Match: MatchCollection's foreach enumerator is the non-generic one, so var infers object.
            foreach (Match match in SeamOwnedUpsertCall.Matches(text))
            {
                var owner = SeamOwners[match.Groups["set"].Value];
                if (!string.Equals(fileName, owner, StringComparison.Ordinal))
                {
                    var line = text.AsSpan(0, match.Index).Count('\n') + 1;
                    violations.Add($"{file}:{line} calls an upsert primitive on db.{match.Groups["set"].Value}; route it through {owner}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Seam-owned DbSets must be written only via their upsert service:\n" + string.Join('\n', violations));
    }

    // Guards the guard: every seam file must still match the regex at least once, so a rename of
    // a DbSet, seam service, or the upsert extensions can't turn the rule above vacuously green.
    [Fact]
    public void EverySeamService_StillContainsAMatchableUpsertCall()
    {
        var matchedFiles = EnumerateSourceFiles()
            .Where(f => SeamOwnedUpsertCall.IsMatch(f.Text))
            .Select(f => Path.GetFileName(f.File))
            .ToHashSet(StringComparer.Ordinal);

        var silentSeams = SeamOwners.Values.Distinct().Where(owner => !matchedFiles.Contains(owner)).ToList();

        Assert.True(silentSeams.Count == 0,
            "Seam services with no matchable upsert call (renamed set/service/extension? update SeamOwners and the regex):\n"
            + string.Join('\n', silentSeams));
    }

    private static IEnumerable<(string File, string Text)> EnumerateSourceFiles()
    {
        var srcRoot = Path.Combine(FindRepoRoot(), "src");
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(srcRoot, file);
            var segments = relative.Split(Path.DirectorySeparatorChar);
            if (segments.Contains("obj") || segments.Contains("bin") || segments.Contains("Migrations"))
                continue;

            yield return (relative, File.ReadAllText(file));
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WojtusDiscord.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (WojtusDiscord.slnx) above the test bin directory.");
    }
}
