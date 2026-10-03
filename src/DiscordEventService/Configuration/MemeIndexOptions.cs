namespace DiscordEventService.Configuration;

internal sealed class MemeIndexOptions
{
    public const string SectionName = "MemeIndex";

    // Meme channels (see CONTEXT.md): channels whose image attachments are in
    // scope for meme indexing. Channel snowflakes, not names.
    public ulong[] ChannelIds { get; set; } = [];

    // Hard ceiling on image size we download for analysis.
    public int MaxImageBytes { get; set; } = 25 * 1024 * 1024;

    // Cost guardrail (#221): at most this many attachments are processed per
    // indexing run. Re-trigger to continue — runs are incremental.
    public int MaxImagesPerRun { get; set; } = 500;

    // The two paths that call the paid model with no human trigger: the live hook on every new
    // meme and the weekly sweep. Off by default (#369): ChannelIds alone must not start spending,
    // because the import needs ChannelIds too. The manual backfill endpoint does not read this.
    public bool AutomaticIndexing { get; set; }

    // Shared secret for POST /api/ops/meme-annotations/import (#369), sent in the
    // X-Import-Secret header. Unset = the endpoint refuses every request. Goes away with #339.
    public string? ImportSecret { get; set; }

    public bool IsConfigured => ChannelIds.Length > 0;
}
