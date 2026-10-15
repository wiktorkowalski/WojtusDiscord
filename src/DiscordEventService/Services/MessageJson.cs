using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordEventService.Infrastructure;
using DSharpPlus.Entities;

namespace DiscordEventService.Services;

// The jsonb shapes of attachments_json and embeds_json, shared by every writer: the live handler,
// the backfill job and the boot quick-sync. null when the message has none.
internal static class MessageJson
{
    private static readonly JsonSerializerOptions EmbedOptions = new() { Converters = { new DiscordUriJsonConverter() } };

    public static string? Attachments(IReadOnlyList<DiscordAttachment> attachments) =>
        attachments.Count > 0
            ? JsonSerializer.Serialize(attachments.Select(a => new StoredAttachment(a.Id, a.Url, a.FileName, a.FileSize)))
            : null;

    public static string? Embeds(IReadOnlyList<DiscordEmbed> embeds) =>
        embeds.Count > 0 ? JsonSerializer.Serialize(embeds, EmbedOptions) : null;

    // A MESSAGE_UPDATE is a real change only when one of these differs (#413). Discord also sends
    // one for an old message when it re-signs an attachment URL (?ex=&is=&hm=) or refreshes an
    // embed, and the stored embed may still have an older, narrower shape than the live one. So
    // attachments compare by Id only and embeds by Url, Title and Description only; a missing
    // embed or attachment counts as a change, which keeps the first link unfurl (#73).
    public static bool IsRealChange(MessageState before, MessageState after) =>
        before.Content != after.Content
        || before.Flags != after.Flags
        || !AttachmentIds(before.AttachmentsJson).SetEquals(AttachmentIds(after.AttachmentsJson))
        || !EmbedKeys(before.EmbedsJson).SequenceEqual(EmbedKeys(after.EmbedsJson));

    private static HashSet<string?> AttachmentIds(string? json) =>
        Items(json).Select(a => a?[nameof(StoredAttachment.Id)]?.ToString()).ToHashSet();

    private static List<(string?, string?, string?)> EmbedKeys(string? json) =>
        Items(json).Select(e => (e?["Url"]?.ToString(), e?["Title"]?.ToString(), e?["Description"]?.ToString())).ToList();

    private static JsonArray Items(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonNode.Parse(json) as JsonArray ?? [];
}

// One element of attachments_json. Url carries Discord's signed CDN query, which expires.
internal sealed record StoredAttachment(ulong Id, string? Url, string? FileName, int FileSize);

// The parts of a message that an edit can change, as stored: normalized content, the two jsonb
// columns and the flags. Flags is null when no before-state is known.
internal sealed record MessageState(string? Content, string? AttachmentsJson, string? EmbedsJson, int? Flags);
