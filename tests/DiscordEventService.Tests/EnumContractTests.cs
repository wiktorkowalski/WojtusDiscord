using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DiscordEventService.Data;
using Xunit;

namespace DiscordEventService.Tests;

// #359: EF stores every enum as its int, so a renumbered or deleted member silently remaps
// existing rows while build, tests and review stay green. This snapshot makes that change red.
public sealed class EnumContractTests
{
    private const string BaselineFileName = "EnumContract.baseline.json";
    private const string UpdateEnvVar = "UPDATE_ENUM_BASELINE";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Fact]
    public void EveryPersistedEnum_MatchesTheCommittedBaseline()
    {
        var current = CaptureEnums();
        var baselinePath = BaselinePath();

        if (Environment.GetEnvironmentVariable(UpdateEnvVar) == "1")
        {
            File.WriteAllText(baselinePath, JsonSerializer.Serialize(current, JsonOptions) + "\n");
            return;
        }

        var baseline = JsonSerializer.Deserialize<SortedDictionary<string, SortedDictionary<string, long>>>(
            File.ReadAllText(baselinePath))!;

        var diffs = Diff(baseline, current);
        if (diffs.Count > 0)
            Assert.Fail(
                $"Enum contract changed. Existing rows store these values as ints:\n{string.Join("\n", diffs)}\n\n" +
                $"A changed or removed value remaps data already in the database — restore it. " +
                $"If the change is a deliberate addition, rerun with {UpdateEnvVar}=1 to rewrite " +
                $"tests/DiscordEventService.Tests/{BaselineFileName} and commit it.");
    }

    private static SortedDictionary<string, SortedDictionary<string, long>> CaptureEnums()
    {
        var enums = typeof(DiscordDbContext).Assembly.GetTypes()
            .Where(t => t.IsEnum && t.FullName!.StartsWith("DiscordEventService.", StringComparison.Ordinal));

        var result = new SortedDictionary<string, SortedDictionary<string, long>>(StringComparer.Ordinal);
        foreach (var type in enums)
        {
            var members = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                members[field.Name] = Convert.ToInt64(field.GetRawConstantValue());
            result[type.FullName!] = members;
        }

        return result;
    }

    private static List<string> Diff(
        SortedDictionary<string, SortedDictionary<string, long>> baseline,
        SortedDictionary<string, SortedDictionary<string, long>> current)
    {
        List<string> diffs = [];

        foreach (var (enumName, baseMembers) in baseline)
        {
            if (!current.TryGetValue(enumName, out var members))
            {
                diffs.Add($"- {enumName}: enum removed or renamed");
                continue;
            }

            foreach (var (member, oldValue) in baseMembers)
            {
                if (!members.TryGetValue(member, out var newValue))
                    diffs.Add($"- {enumName}.{member}: member removed (was {oldValue})");
                else if (newValue != oldValue)
                    diffs.Add($"- {enumName}.{member}: value changed {oldValue} -> {newValue}");
            }

            foreach (var (member, newValue) in members.Where(m => !baseMembers.ContainsKey(m.Key)))
                diffs.Add($"- {enumName}.{member}: member added (= {newValue}), not in baseline");
        }

        foreach (var enumName in current.Keys.Where(k => !baseline.ContainsKey(k)))
            diffs.Add($"- {enumName}: enum added, not in baseline");

        return diffs;
    }

    // The test runs from bin/, so locate the committed baseline next to this source file.
    private static string BaselinePath([CallerFilePath] string sourceFile = "")
        => Path.Combine(Path.GetDirectoryName(sourceFile)!, BaselineFileName);
}
