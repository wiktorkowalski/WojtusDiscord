using System.Diagnostics.CodeAnalysis;
using DiscordEventService.Configuration;

namespace DiscordEventService.Services.MemeIndexing;

// One benchmark column, configured as "model" or "model|effort=low" (#366).
internal sealed record BenchmarkSlot(string Model, string? ReasoningEffort)
{
    private const string EffortOverride = "effort";

    // The report groups by this, so it must describe the whole request: a slot without an effort
    // sends no `reasoning` field and does NOT inherit OpenRouter:ReasoningEffort.
    public string Key => ReasoningEffort is null ? Model : $"{Model}|{EffortOverride}={ReasoningEffort}";

    public static bool TryParseAll(
        IReadOnlyList<string> configured,
        out List<BenchmarkSlot> slots,
        [NotNullWhen(false)] out string? error)
    {
        slots = [];
        if (configured.Count == 0)
        {
            error = "OpenRouter:BenchmarkModels is empty — set OpenRouter__BenchmarkModels__0..N " +
                    "(\"model\" or \"model|effort=low\")";
            return false;
        }

        foreach (var value in configured)
        {
            if (!TryParse(value, out var slot, out error))
                return false;
            slots.Add(slot);
        }

        error = null;
        return true;
    }

    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out BenchmarkSlot? slot,
        [NotNullWhen(false)] out string? error)
    {
        slot = null;
        var parts = (value ?? string.Empty).Split('|', StringSplitOptions.TrimEntries);

        var model = parts[0];
        if (model.Length == 0)
        {
            error = $"Benchmark slot '{value}' has no model id";
            return false;
        }

        string? effort = null;
        foreach (var part in parts.Skip(1))
        {
            var separator = part.IndexOf('=');
            var name = separator < 0 ? part : part[..separator].TrimEnd();
            var setting = separator < 0 ? string.Empty : part[(separator + 1)..].TrimStart();

            if (name != EffortOverride)
            {
                error = $"Benchmark slot '{value}': unknown override '{name}' (supported: {EffortOverride})";
                return false;
            }

            if (effort is not null)
            {
                error = $"Benchmark slot '{value}': '{EffortOverride}' is set more than once";
                return false;
            }

            if (!OpenRouterOptions.IsKnownReasoningEffort(setting))
            {
                error = $"Benchmark slot '{value}': effort '{setting}' is not one of {OpenRouterOptions.KnownReasoningEfforts}";
                return false;
            }

            effort = setting;
        }

        slot = new BenchmarkSlot(model, effort);
        error = null;
        return true;
    }
}
