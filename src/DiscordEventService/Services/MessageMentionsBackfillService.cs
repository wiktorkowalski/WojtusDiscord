using System.Text.RegularExpressions;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

public partial class MessageMentionsBackfillService(
    DiscordDbContext db,
    ILogger<MessageMentionsBackfillService> logger)
{
    public record Result(
        int MessagesScanned,
        int MessagesSkipped,
        int MessagesProcessed,
        int MentionsCreated);

    [GeneratedRegex(@"<@!?(\d+)>")]
    private static partial Regex UserMentionRegex();

    [GeneratedRegex(@"<@&(\d+)>")]
    private static partial Regex RoleMentionRegex();

    [GeneratedRegex(@"<#(\d+)>")]
    private static partial Regex ChannelMentionRegex();

    public async Task<Result> BackfillAsync(CancellationToken ct)
    {
        var alreadyProcessed = await db.MessageMentions
            .Select(m => m.MessageId)
            .Distinct()
            .ToHashSetAsync(ct);

        var messages = await db.Messages
            .Where(m => !m.IsDeleted && m.Content != null && m.Content != "")
            .Select(m => new { m.Id, m.Content })
            .ToListAsync(ct);

        logger.LogInformation("Mention backfill starting: {MessageCount} messages with content, {SkippedCount} already have mentions",
            messages.Count, alreadyProcessed.Count);

        var scanned = messages.Count;
        var skipped = 0;
        var processed = 0;
        var mentionsCreated = 0;

        foreach (var msg in messages)
        {
            ct.ThrowIfCancellationRequested();

            if (alreadyProcessed.Contains(msg.Id))
            {
                skipped++;
                continue;
            }

            var mentions = ExtractMentions(msg.Id, msg.Content!);
            if (mentions.Count == 0)
                continue;

            db.MessageMentions.AddRange(mentions);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            processed++;
            mentionsCreated += mentions.Count;
        }

        logger.LogInformation("Mention backfill done: {ProcessedCount} messages processed, {MentionCount} mentions created",
            processed, mentionsCreated);

        return new Result(scanned, skipped, processed, mentionsCreated);
    }

    private static List<MessageMentionEntity> ExtractMentions(Guid messageId, string content)
    {
        var mentions = new List<MessageMentionEntity>();
        var seen = new HashSet<(MessageMentionType, ulong?)>();

        foreach (Match match in UserMentionRegex().Matches(content))
        {
            if (ulong.TryParse(match.Groups[1].Value, out var userId) && seen.Add((MessageMentionType.User, userId)))
            {
                mentions.Add(new MessageMentionEntity
                {
                    MessageId = messageId,
                    MentionedUserDiscordId = userId,
                    MentionType = MessageMentionType.User
                });
            }
        }

        foreach (Match match in RoleMentionRegex().Matches(content))
        {
            if (ulong.TryParse(match.Groups[1].Value, out var roleId) && seen.Add((MessageMentionType.Role, roleId)))
            {
                mentions.Add(new MessageMentionEntity
                {
                    MessageId = messageId,
                    MentionedRoleDiscordId = roleId,
                    MentionType = MessageMentionType.Role
                });
            }
        }

        foreach (Match match in ChannelMentionRegex().Matches(content))
        {
            if (ulong.TryParse(match.Groups[1].Value, out var channelId) && seen.Add((MessageMentionType.Channel, channelId)))
            {
                mentions.Add(new MessageMentionEntity
                {
                    MessageId = messageId,
                    MentionedChannelDiscordId = channelId,
                    MentionType = MessageMentionType.Channel
                });
            }
        }

        if (content.Contains("@everyone", StringComparison.Ordinal))
            mentions.Add(new MessageMentionEntity { MessageId = messageId, MentionType = MessageMentionType.Everyone });

        if (content.Contains("@here", StringComparison.Ordinal))
            mentions.Add(new MessageMentionEntity { MessageId = messageId, MentionType = MessageMentionType.Here });

        return mentions;
    }
}
