namespace DiscordEventService.Data.Entities.Core;

// The closed set behind meme_annotations.source (#368): one list for the json_schema enum,
// the parse check and the check constraint, so "twitter" / "x" / "Twitter" stay one value.
internal static class MemeSources
{
    // Contract only: the model says "none" when no platform is visible. Stored as NULL.
    public const string None = "none";

    // A visible platform that is not on the list. A bucket, not a name: search skips it.
    public const string Other = "other";

    public static readonly string[] Known =
    [
        "reddit", "twitter", "facebook", "instagram", "tiktok", "youtube", "discord",
        "kwejk", "jbzd", "jeja", "wykop", "demotywatory", "blasty", "memisko",
        "imgflip", "9gag", "ifunny", Other,
    ];

    public static readonly string CheckConstraintSql =
        $"source IS NULL OR source IN ({string.Join(", ", Known.Select(s => $"'{s}'"))})";
}
