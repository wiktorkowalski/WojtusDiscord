namespace DiscordEventService.Configuration;

public sealed class MemeIndexOptions
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

    public bool IsConfigured => ChannelIds.Length > 0;
}
