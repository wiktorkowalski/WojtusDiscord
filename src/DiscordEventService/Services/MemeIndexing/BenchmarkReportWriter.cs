using System.Globalization;
using System.Text;

namespace DiscordEventService.Services.MemeIndexing;

// Slot is the BenchmarkSlot.Key the cell ran under ("model" or "model|effort=low").
internal sealed record BenchmarkCell(string Slot, MemeAnalysisResult Result, double ElapsedSeconds);

internal sealed record BenchmarkItem(
    MemeSampleItem Sample,
    string? FreshUrl,
    string? SkipReason,
    List<BenchmarkCell> Cells);

internal sealed record BenchmarkRun(
    DateTime StartedUtc,
    DateTime FinishedUtc,
    int RequestedSampleSize,
    string[] Slots,
    List<BenchmarkItem> Items);

internal static class BenchmarkReportWriter
{
    public static string Render(BenchmarkRun run)
    {
        // Invariant culture throughout — the host's locale (PL: comma decimals)
        // must not leak into a machine-greppable report.
        var sb = new StringBuilder();
        sb.AppendLine(Inv($"# Meme model benchmark — {run.StartedUtc:yyyy-MM-dd HH:mm} UTC"));
        sb.AppendLine();
        sb.AppendLine(Inv($"Sample: {run.Items.Count} images (requested {run.RequestedSampleSize}), ") +
                      Inv($"duration {(run.FinishedUtc - run.StartedUtc).TotalMinutes:F1} min."));
        sb.AppendLine();

        RenderTotals(sb, run);

        var skipped = run.Items.Where(i => i.SkipReason is not null).ToList();
        if (skipped.Count > 0)
        {
            sb.AppendLine($"## Skipped images ({skipped.Count})");
            sb.AppendLine();
            foreach (var item in skipped)
                sb.AppendLine($"- {Escape(item.Sample.FileName)} ({item.Sample.CreatedAtUtc.Year}) — {Escape(item.SkipReason!)} — {JumpLink(item.Sample)}");
            sb.AppendLine();
        }

        sb.AppendLine("## Per-meme comparison");
        sb.AppendLine();

        var index = 0;
        foreach (var item in run.Items.Where(i => i.SkipReason is null))
        {
            index++;
            RenderItem(sb, index, item);
        }

        return sb.ToString();
    }

    private static void RenderTotals(StringBuilder sb, BenchmarkRun run)
    {
        sb.AppendLine("## Totals");
        sb.AppendLine();
        sb.AppendLine("| slot | ok | refused | failed | prompt tok | completion tok | cost USD | avg sec |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");

        // One row per slot, matched by position: an item's cells are in slot order. Matching by
        // name would merge a slot listed twice (a noise measurement) into one doubled row (#366).
        foreach (var (position, slot) in run.Slots.Index())
        {
            var cells = run.Items.Where(i => i.Cells.Count > position).Select(i => i.Cells[position]).ToList();
            var ok = cells.Count(c => c.Result.Outcome == MemeAnalysisOutcome.Success);
            var refused = cells.Count(c => c.Result.Outcome == MemeAnalysisOutcome.Refusal);
            var failed = cells.Count(c => c.Result.Outcome == MemeAnalysisOutcome.Error);
            var promptTokens = cells.Sum(c => c.Result.Usage?.PromptTokens ?? 0);
            var completionTokens = cells.Sum(c => c.Result.Usage?.CompletionTokens ?? 0);
            var cost = cells.Sum(c => c.Result.Usage?.CostUsd ?? 0);
            var avgSeconds = cells.Count > 0 ? cells.Average(c => c.ElapsedSeconds) : 0;

            sb.AppendLine(Inv($"| {Escape(slot)} | {ok} | {refused} | {failed} | {promptTokens} | {completionTokens} | {cost:F4} | {avgSeconds:F1} |"));
        }

        sb.AppendLine();
    }

    private static void RenderItem(StringBuilder sb, int index, BenchmarkItem item)
    {
        sb.AppendLine(Inv($"### {index}. {Escape(item.Sample.FileName)} ({item.Sample.CreatedAtUtc:yyyy-MM-dd}) — [jump]({JumpLink(item.Sample)})"));
        sb.AppendLine();
        if (item.FreshUrl is not null)
        {
            // Fresh signed CDN URL — stays valid only ~24h after the run.
            sb.AppendLine($"![meme]({item.FreshUrl})");
            sb.AppendLine();
        }

        sb.AppendLine($"| field | {string.Join(" | ", item.Cells.Select(c => Escape(c.Slot)))} |");
        sb.AppendLine($"|---{string.Concat(Enumerable.Repeat("|---", item.Cells.Count))}|");
        AppendRow(sb, "outcome", item.Cells, c => c.Result.Outcome == MemeAnalysisOutcome.Error
            ? $"{c.Result.Outcome}: {c.Result.Error}"
            : c.Result.Outcome.ToString());
        AppendRow(sb, "description_pl", item.Cells, c => c.Result.Metadata?.DescriptionPl);
        AppendRow(sb, "description_en", item.Cells, c => c.Result.Metadata?.DescriptionEn);
        AppendRow(sb, "ocr_text", item.Cells, c => c.Result.Metadata?.OcrText);
        AppendRow(sb, "tags", item.Cells, c => JoinOrNull(c.Result.Metadata?.Tags));
        AppendRow(sb, "image_kind", item.Cells, c => c.Result.Metadata?.ImageKind is { } kind ? MemeJsonNames.Of(kind) : null);
        AppendRow(sb, "templates", item.Cells, c => JoinOrNull(c.Result.Metadata?.Templates));
        AppendRow(sb, "people", item.Cells,
            c => JoinOrNull(c.Result.Metadata?.People?.Where(p => p is not null).Select(p => $"{p.Name} ({MemeJsonNames.Of(p.Evidence)})")));
        AppendRow(sb, "search_phrases", item.Cells, c => JoinOrNull(c.Result.Metadata?.SearchPhrases));
        AppendRow(sb, "franchise", item.Cells, c => c.Result.Metadata?.Franchise);
        AppendRow(sb, "source", item.Cells, c => c.Result.Metadata?.Source);
        AppendRow(sb, "language", item.Cells, c => c.Result.Metadata?.Language is { } language ? MemeJsonNames.Of(language) : null);
        sb.AppendLine();
    }

    private static void AppendRow(StringBuilder sb, string field, List<BenchmarkCell> cells, Func<BenchmarkCell, string?> value) =>
        sb.AppendLine($"| {field} | {string.Join(" | ", cells.Select(c => Escape(value(c) ?? "—")))} |");

    // An empty list renders as the dash too: "none" reads the same as "not returned" in a comparison table.
    private static string? JoinOrNull(IEnumerable<string>? values) =>
        values is null ? null : string.Join(", ", values) is { Length: > 0 } joined ? joined : null;

    private static string JumpLink(MemeSampleItem sample) =>
        $"https://discord.com/channels/{sample.GuildDiscordId}/{sample.ChannelDiscordId}/{sample.MessageDiscordId}";

    // Markdown-table safety: pipes break columns, newlines break rows.
    private static string Escape(string value) =>
        value.Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");

    private static string Inv(FormattableString value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
