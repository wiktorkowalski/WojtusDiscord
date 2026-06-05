using DiscordEventService.Data;
using DiscordEventService.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Controllers;

/// <summary>
/// Rich, hand-crafted views over the core current-state entities (users, channels,
/// members, messages) with name-resolved joins and detail drill-downs. All reads
/// are AsNoTracking; joins are core-to-core by Guid FK.
/// </summary>
[ApiController]
[Route("api/entities")]
public sealed class EntitiesController(DiscordDbContext db) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    [HttpGet("users")]
    public Task<PagedResult<UserListDto>> GetUsers(
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = $"%{search}%";
            query = query.Where(u => EF.Functions.ILike(u.Username, like)
                || (u.GlobalName != null && EF.Functions.ILike(u.GlobalName, like)));
        }

        return PageAsync(
            query.OrderByDescending(u => u.LastUpdatedUtc).ThenByDescending(u => u.Id)
                .Select(u => new UserListDto(
                    u.Id, u.DiscordId, u.Username, u.GlobalName, u.IsBot, u.IsSystem,
                    u.FirstSeenUtc, u.LastUpdatedUtc, u.AvatarHash)),
            page, pageSize, ct);
    }

    [HttpGet("users/{id:guid}")]
    public async Task<ActionResult<UserDetailDto>> GetUser(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id,
                u.DiscordId,
                u.Username,
                u.GlobalName,
                u.Discriminator,
                u.IsBot,
                u.IsSystem,
                u.FirstSeenUtc,
                u.LastUpdatedUtc,
                u.AvatarHash,
                MembershipCount = u.Memberships.Count,
            })
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return NotFound();
        }

        var history = await db.UserNameHistory.AsNoTracking()
            .Where(h => h.UserId == id)
            .OrderByDescending(h => h.ChangedAtUtc)
            .Select(h => new NameChangeDto(
                h.UsernameBefore, h.UsernameAfter, h.GlobalNameBefore, h.GlobalNameAfter, h.ChangedAtUtc))
            .ToListAsync(ct);

        return Ok(new UserDetailDto(
            user.Id, user.DiscordId, user.Username, user.GlobalName, user.Discriminator,
            user.IsBot, user.IsSystem, user.FirstSeenUtc, user.LastUpdatedUtc, user.MembershipCount, history,
            user.AvatarHash));
    }

    [HttpGet("channels")]
    public Task<PagedResult<ChannelListDto>> GetChannels(
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize, CancellationToken ct = default)
        => PageAsync(
            db.Channels.AsNoTracking()
                .OrderBy(c => c.Position).ThenBy(c => c.Name).ThenBy(c => c.Id)
                .Select(c => new ChannelListDto(
                    c.Id, c.DiscordId, c.Name, c.Type.ToString(), c.ParentDiscordId,
                    c.IsNsfw, c.Position, c.IsDeleted)),
            page, pageSize, ct);

    [HttpGet("channels/{id:guid}")]
    public async Task<ActionResult<ChannelDetailDto>> GetChannel(Guid id, CancellationToken ct)
    {
        var channel = await db.Channels.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id,
                c.DiscordId,
                c.Name,
                c.Type,
                c.Topic,
                c.ParentDiscordId,
                c.IsNsfw,
                c.Position,
                c.IsDeleted,
                MessageCount = db.Messages.LongCount(m => m.ChannelId == c.Id),
            })
            .FirstOrDefaultAsync(ct);

        if (channel is null)
        {
            return NotFound();
        }

        return Ok(new ChannelDetailDto(
            channel.Id, channel.DiscordId, channel.Name, channel.Type.ToString(), channel.Topic,
            channel.ParentDiscordId, channel.IsNsfw, channel.Position, channel.IsDeleted, channel.MessageCount));
    }

    [HttpGet("members")]
    public Task<PagedResult<MemberListDto>> GetMembers(
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize, CancellationToken ct = default)
        => PageAsync(
            db.Members.AsNoTracking()
                .OrderBy(m => m.User.Username).ThenBy(m => m.Id)
                .Select(m => new MemberListDto(
                    m.Id, m.UserId, m.User.DiscordId, m.User.Username, m.Nickname,
                    m.JoinedAtUtc, m.IsPending, m.TimeoutUntilUtc, m.User.AvatarHash, m.GuildAvatarHash)),
            page, pageSize, ct);

    [HttpGet("messages")]
    public Task<PagedResult<MessageListDto>> GetMessages(
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] Guid? channelId = null, CancellationToken ct = default)
    {
        var query = db.Messages.AsNoTracking();
        if (channelId is not null)
        {
            query = query.Where(m => m.ChannelId == channelId.Value);
        }

        return PageAsync(
            query.OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
                .Select(m => new MessageListDto(
                    m.Id, m.DiscordId, m.Content,
                    m.Author.DiscordId, m.Author.Username,
                    m.Channel.DiscordId, m.Channel.Name,
                    m.HasAttachments, m.HasEmbeds, m.IsDeleted, m.CreatedAtUtc, m.EditedAtUtc,
                    m.Author.AvatarHash)),
            page, pageSize, ct);
    }

    [HttpGet("messages/{id:guid}")]
    public async Task<ActionResult<MessageDetailDto>> GetMessage(Guid id, CancellationToken ct)
    {
        var message = await db.Messages.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new
            {
                m.Id,
                m.DiscordId,
                m.Content,
                AuthorDiscordId = m.Author.DiscordId,
                AuthorName = m.Author.Username,
                ChannelDiscordId = m.Channel.DiscordId,
                ChannelName = m.Channel.Name,
                m.HasAttachments,
                m.HasEmbeds,
                m.AttachmentsJson,
                m.EmbedsJson,
                m.IsDeleted,
                m.DeletedAtUtc,
                m.CreatedAtUtc,
                m.EditedAtUtc,
            })
            .FirstOrDefaultAsync(ct);

        if (message is null)
        {
            return NotFound();
        }

        var edits = await db.MessageEditHistory.AsNoTracking()
            .Where(h => h.MessageId == id)
            .OrderByDescending(h => h.EditedAtUtc)
            .Select(h => new MessageEditDto(h.ContentBefore, h.ContentAfter, h.EditedAtUtc, h.RecordedAtUtc))
            .ToListAsync(ct);

        return Ok(new MessageDetailDto(
            message.Id, message.DiscordId, message.Content,
            message.AuthorDiscordId, message.AuthorName, message.ChannelDiscordId, message.ChannelName,
            message.HasAttachments, message.HasEmbeds, message.AttachmentsJson, message.EmbedsJson,
            message.IsDeleted, message.DeletedAtUtc, message.CreatedAtUtc, message.EditedAtUtc, edits));
    }

    private static async Task<PagedResult<T>> PageAsync<T>(
        IQueryable<T> query, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var total = await query.LongCountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, page, pageSize);
    }
}
