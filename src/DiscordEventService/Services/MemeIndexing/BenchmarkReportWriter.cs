using System.Globalization;
using System.Text;

namespace DiscordEventService.Services.MemeIndexing;

public sealed record BenchmarkCell(string Model, MemeAnalysisResult Result, double ElapsedSeconds);

public sealed record BenchmarkItem(
    MemeSampleItem Sample,
    string? FreshUrl,
    string? SkipReason,
    List<BenchmarkCell> Cells);

public sealed record BenchmarkRun(
    DateTime StartedUtc,
    DateTime FinishedUtc,
    int RequestedSampleSize,
    string[] Models,
    List<BenchmarkItem> Items);

public static class BenchmarkReportWriter
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
        sb.AppendLine("| model | ok | refused | failed | prompt tok | completion tok | cost USD | avg sec |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");

        foreach (var model in run.Models)
        {
            var cells = run.Items.SelectMany(i => i.Cells).Where(c => c.Model == model).ToList();
            var ok = cells.Count(c => c.Result.Outcome == MemeAnalysisOutcome.Success);
            var refused = cells.Count(c => c.Result.Outcome == MemeAnalysisOutcome.Refusal);
            var failed = cells.Count(c => c.Result.Outcome == MemeAnalysisOutcome.Error);
            var promptTokens = cells.Sum(c => c.Result.Usage?.PromptTokens ?? 0);
            var completionTokens = cells.Sum(c => c.Result.Usage?.CompletionTokens ?? 0);
            var cost = cells.Sum(c => c.Result.Usage?.CostUsd ?? 0);
            var avgSeconds = cells.Count > 0 ? cells.Average(c => c.ElapsedSeconds) : 0;

            sb.AppendLine(Inv($"| {model} | {ok} | {refused} | {failed} | {promptTokens} | {completionTokens} | {cost:F4} | {avgSeconds:F1} |"));
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

        sb.AppendLine($"| field | {string.Join(" | ", item.Cells.Select(c => c.Model))} |");
        sb.AppendLine($"|---{string.Concat(Enumerable.Repeat("|---", item.Cells.Count))}|");
        AppendRow(sb, "outcome", item.Cells, c => c.Result.Outcome == MemeAnalysisOutcome.Error
            ? $"{c.Result.Outcome}: {c.Result.Error}"
            : c.Result.Outcome.ToString());
        AppendRow(sb, "description_pl", item.Cells, c => c.Result.Metadata?.DescriptionPl);
        AppendRow(sb, "description_en", item.Cells, c => c.Result.Metadata?.DescriptionEn);
        AppendRow(sb, "ocr_text", item.Cells, c => c.Result.Metadata?.OcrText);
        AppendRow(sb, "tags", item.Cells, c => c.Result.Metadata is { } m ? string.Join(", ", m.Tags) : null);
        AppendRow(sb, "source", item.Cells, c => c.Result.Metadata?.Source);
        AppendRow(sb, "template", item.Cells, c => c.Result.Metadata?.Template);
        sb.AppendLine();
    }

    private static void AppendRow(StringBuilder sb, string field, List<BenchmarkCell> cells, Func<BenchmarkCell, string?> value) =>
        sb.AppendLine($"| {field} | {string.Join(" | ", cells.Select(c => Escape(value(c) ?? "—")))} |");

    private static string JumpLink(MemeSampleItem sample) =>
        $"https://discord.com/channels/{sample.GuildDiscordId}/{sample.ChannelDiscordId}/{sample.MessageDiscordId}";

    // Markdown-table safety: pipes break columns, newlines break rows.
    private static string Escape(string value) =>
        value.Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");

    private static string Inv(FormattableString value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
