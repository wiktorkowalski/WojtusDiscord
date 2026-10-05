using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Tests;

// The corpus builder of the meme dashboard tests (#395): one guild, one meme channel, one author.
// Every meme has its own message, and the message holds the attachment in attachments_json.
internal sealed class MemeStatsTestData(DiscordDbContext db)
{
    // Above 2^53: a JSON number would lose digits in a browser.
    public const ulong GuildDiscordId = 900000000000000001UL;
    public const ulong ChannelDiscordId = 900000000000000002UL;
    public const string ChannelName = "memes";
    public const string ModelA = "model/a";
    public const string ModelB = "model/b";

    private GuildEntity _guild = null!;
    private ChannelEntity _channel = null!;
    private UserEntity _author = null!;

    public static ulong MessageIdOf(ulong attachmentDiscordId) => attachmentDiscordId + 5_000_000UL;

    public static string StoredUrlOf(ulong attachmentDiscordId) =>
        $"https://cdn.discordapp.com/attachments/{ChannelDiscordId}/{attachmentDiscordId}/meme-{attachmentDiscordId}.png";

    public async Task ResetAsync()
    {
        await db.MemeSearchLog.ExecuteDeleteAsync();
        await db.MemeAnnotations.ExecuteDeleteAsync();
        await db.MemeIndex.ExecuteDeleteAsync();
        await db.Messages.ExecuteDeleteAsync();
        await db.Channels.ExecuteDeleteAsync();
        await db.Users.ExecuteDeleteAsync();
        await db.Guilds.ExecuteDeleteAsync();

        _guild = new GuildEntity { DiscordId = GuildDiscordId, Name = "g" };
        db.Guilds.Add(_guild);
        await db.SaveChangesAsync();

        _channel = new ChannelEntity { DiscordId = ChannelDiscordId, GuildId = _guild.Id, Name = ChannelName, Type = ChannelType.Text };
        _author = new UserEntity { DiscordId = 5UL, Username = "u" };
        db.AddRange(_channel, _author);
        await db.SaveChangesAsync();
    }

    // An image in the meme channel with no meme_index row.
    public async Task<MessageEntity> AddImageMessageAsync(
        ulong attachmentDiscordId, DateTime? postedAtUtc = null, bool messageDeleted = false)
    {
        var message = new MessageEntity
        {
            DiscordId = MessageIdOf(attachmentDiscordId),
            ChannelId = _channel.Id,
            GuildId = _guild.Id,
            AuthorId = _author.Id,
            HasAttachments = true,
            // The 4-field PascalCase shape MessageEventHandler/MessagesBackfillJob serialize.
            AttachmentsJson =
                $"[{{\"Id\":{attachmentDiscordId},\"Url\":\"{StoredUrlOf(attachmentDiscordId)}?ex=expired\"," +
                $"\"FileName\":\"meme-{attachmentDiscordId}.png\",\"FileSize\":123}}]",
            CreatedAtUtc = postedAtUtc ?? DateTime.UtcNow,
            IsDeleted = messageDeleted,
            DeletedAtUtc = messageDeleted ? DateTime.UtcNow : null,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    public async Task<MemeIndexEntity> AddMemeAsync(
        ulong attachmentDiscordId,
        MemeIndexStatus status = MemeIndexStatus.Indexed,
        DateTime? postedAtUtc = null,
        bool messageDeleted = false,
        long fileSizeBytes = 100,
        bool refused = false)
    {
        var message = await AddImageMessageAsync(attachmentDiscordId, postedAtUtc, messageDeleted);

        var meme = new MemeIndexEntity
        {
            MessageId = message.Id,
            GuildDiscordId = GuildDiscordId,
            ChannelDiscordId = ChannelDiscordId,
            MessageDiscordId = message.DiscordId,
            AttachmentDiscordId = attachmentDiscordId,
            FileName = $"meme-{attachmentDiscordId}.png",
            FileSizeBytes = fileSizeBytes,
            ContentType = "image/png",
            Status = status,
            // ck_meme_index_status: a Failed or Skipped row must say why.
            Error = status is MemeIndexStatus.Failed or MemeIndexStatus.Skipped ? "seeded by test" : null,
            RefusedByModelId = refused ? ModelA : null,
            RefusedByPromptVersion = refused ? OpenRouterClient.PromptVersion : null,
        };
        db.MemeIndex.Add(meme);
        await db.SaveChangesAsync();
        return meme;
    }

    public async Task AddAnnotationAsync(
        MemeIndexEntity meme,
        string modelId = ModelA,
        DateTime? indexedAtUtc = null,
        Action<MemeAnnotationEntity>? configure = null)
    {
        var annotation = new MemeAnnotationEntity
        {
            MemeIndexId = meme.Id,
            AttachmentDiscordId = meme.AttachmentDiscordId,
            ModelId = modelId,
            PromptVersion = OpenRouterClient.PromptVersion,
            IndexedAtUtc = indexedAtUtc ?? DateTime.UtcNow,
            DescriptionPl = $"opis {meme.AttachmentDiscordId} {modelId}",
            DescriptionEn = "english description",
            RawResponseJson = "{}",
        };
        configure?.Invoke(annotation);
        db.MemeAnnotations.Add(annotation);
        await db.SaveChangesAsync();
    }

    // An indexed meme with one annotation of ModelA.
    public async Task<MemeIndexEntity> AddIndexedAsync(
        ulong attachmentDiscordId, Action<MemeAnnotationEntity>? configure = null, DateTime? postedAtUtc = null)
    {
        var meme = await AddMemeAsync(attachmentDiscordId, postedAtUtc: postedAtUtc);
        await AddAnnotationAsync(meme, configure: configure);
        return meme;
    }

    // One meme_search_log row, written as MemeSearchService writes it. hits = the attachment
    // ids in rank order; each is logged with ModelA as the annotation that won.
    public async Task AddSearchAsync(
        MemeSearchSource source,
        DateTime searchedAtUtc,
        string query,
        double durationMs,
        ulong userDiscordId = 0UL,
        ulong channelDiscordId = 0UL,
        params ulong[] hits)
    {
        db.MemeSearchLog.Add(new MemeSearchLogEntity
        {
            SearchedAtUtc = searchedAtUtc,
            GuildDiscordId = GuildDiscordId,
            ChannelDiscordId = channelDiscordId,
            UserDiscordId = userDiscordId,
            Source = source,
            Query = query,
            Tokens = [query],
            RankTokens = [query],
            ResultLimit = MemeSearchService.DefaultLimit,
            ResultCount = hits.Length,
            DurationMs = durationMs,
            TrigramWeight = 0.5,
            TrigramThreshold = 0.4,
            StopListVersion = MemeSearchService.RankStopListVersion,
            Results = hits
                .Select((attachmentDiscordId, index) => new MemeSearchLogResultEntity
                {
                    Rank = index + 1,
                    AttachmentDiscordId = attachmentDiscordId,
                    ModelId = ModelA,
                    PromptVersion = OpenRouterClient.PromptVersion,
                    TsRank = 0.5,
                    TrigramSimilarity = 1.0,
                    Score = 1.0 - (index * 0.1),
                })
                .ToList(),
        });
        await db.SaveChangesAsync();
    }
}
