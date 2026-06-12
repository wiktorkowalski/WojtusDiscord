using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

internal sealed class UserService(DiscordDbContext db, ILogger<UserService> logger)
{
    public async Task<UpsertResult<Guid>> UpsertUserAsync(DiscordUser user)
    {
        // Pre-query old values for name-history detection: the set-based upsert only does the
        // write, and on a 23505 race we have no reliable "before" to compare against.
        var existing = await db.Users
            .Where(u => u.DiscordId == user.Id)
            .Select(u => new { u.Id, u.Username, u.GlobalName })
            .FirstOrDefaultAsync();

        if (existing is not null && (existing.Username != user.Username || existing.GlobalName != user.GlobalName))
            return await UpdateUserWithNameHistoryAsync(user, existing.Username, existing.GlobalName, existing.Id);

        return await UpsertUserUnchangedAsync(user);
    }

    // A name change is the only flow that issues a SECOND write (the history row) after
    // updating the user. Do it as load-modify-save committed in a SINGLE SaveChanges so the
    // update and its history row land atomically (one implicit transaction, auto-retried under
    // EnableRetryOnFailure). This avoids an explicit transaction + ChangeTracker.Clear, which on
    // this shared DbContext would drop pending rows of batch-staging callers (e.g. the backfill
    // jobs upsert authors mid-batch while messages/reactions are staged for one SaveChanges).
    private async Task<UpsertResult<Guid>> UpdateUserWithNameHistoryAsync(
        DiscordUser user, string? oldUsername, string? oldGlobalName, Guid existingId)
    {
        var usernameChanged = oldUsername != user.Username;
        var globalNameChanged = oldGlobalName != user.GlobalName;

        var entity = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == user.Id);
        if (entity is null)
        {
            logger.LogError("User upsert lost the row for user {UserId} after detecting a name change", user.Id);
            return UpsertResult<Guid>.Failure($"User upsert lost the row for DiscordId={user.Id}");
        }

        entity.Username = user.Username;
        entity.GlobalName = user.GlobalName;
        entity.Discriminator = user.Discriminator;
        entity.AvatarHash = user.AvatarHash;

        db.UserNameHistory.Add(new UserNameHistoryEntity
        {
            UserId = existingId,
            UsernameBefore = usernameChanged ? oldUsername : null,
            UsernameAfter = usernameChanged ? user.Username : null,
            GlobalNameBefore = globalNameChanged ? oldGlobalName : null,
            GlobalNameAfter = globalNameChanged ? user.GlobalName : null,
            ChangedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        logger.LogInformation(
            "User name changed for {UserId}: username {OldUsername} → {NewUsername}, global name {OldGlobalName} → {NewGlobalName}",
            user.Id,
            usernameChanged ? oldUsername : "(unchanged)",
            usernameChanged ? user.Username : "(unchanged)",
            globalNameChanged ? oldGlobalName : "(unchanged)",
            globalNameChanged ? user.GlobalName : "(unchanged)");

        return UpsertResult<Guid>.Success(existingId);
    }

    // No name change (or a brand-new user): a single set-based upsert. One statement, race-safe
    // insert-or-update, and — unlike SaveChanges — it never flushes a caller's staged entities.
    private async Task<UpsertResult<Guid>> UpsertUserUnchangedAsync(DiscordUser user)
    {
        var id = await db.Users.UpsertAsync(
            u => u.DiscordId == user.Id,
            s => s
                .SetProperty(u => u.Username, user.Username)
                .SetProperty(u => u.GlobalName, user.GlobalName)
                .SetProperty(u => u.Discriminator, user.Discriminator)
                .SetProperty(u => u.AvatarHash, user.AvatarHash),
            () => new UserEntity
            {
                DiscordId = user.Id,
                Username = user.Username,
                GlobalName = user.GlobalName,
                Discriminator = user.Discriminator,
                AvatarHash = user.AvatarHash,
                IsBot = user.IsBot,
                IsSystem = user.IsSystem ?? false
            },
            u => u.Id);

        if (id == Guid.Empty)
        {
            logger.LogError("User upsert lost the row for user {UserId} after upsert", user.Id);
            return UpsertResult<Guid>.Failure($"User upsert lost the row for DiscordId={user.Id}");
        }

        return UpsertResult<Guid>.Success(id);
    }

    public async Task UpsertMemberAsync(DiscordMember member)
    {
        var userResult = await UpsertUserAsync(member);

        // User Guid comes back from the upsert; the guild must already exist (we don't upsert it here).
        var guildId = await db.Guilds
            .Where(g => g.DiscordId == member.Guild.Id)
            .Select(g => g.Id)
            .FirstOrDefaultAsync();

        if (!userResult.IsSuccess || guildId == Guid.Empty)
        {
            logger.LogError("Cannot upsert member {MemberId} in guild {GuildId}: user resolved {UserResolved}, guild resolved {GuildResolved}",
                member.Id, member.Guild.Id, userResult.IsSuccess, guildId != Guid.Empty);
            return;
        }

        var userId = userResult.Value;

        await db.Members.UpsertAsync(
            m => m.UserId == userId && m.GuildId == guildId,
            s => s
                .SetProperty(m => m.Nickname, member.Nickname)
                .SetProperty(m => m.GuildAvatarHash, member.GuildAvatarHash)
                .SetProperty(m => m.PremiumSinceUtc, member.PremiumSince != null ? member.PremiumSince.Value.UtcDateTime : (DateTime?)null)
                .SetProperty(m => m.IsDeafened, member.IsDeafened == true)
                .SetProperty(m => m.IsMuted, member.IsMuted == true)
                .SetProperty(m => m.IsPending, member.IsPending == true)
                .SetProperty(m => m.TimeoutUntilUtc, member.CommunicationDisabledUntil != null ? member.CommunicationDisabledUntil.Value.UtcDateTime : (DateTime?)null),
            () => new MemberEntity
            {
                UserId = userId,
                GuildId = guildId,
                Nickname = member.Nickname,
                GuildAvatarHash = member.GuildAvatarHash,
                JoinedAtUtc = member.JoinedAt.UtcDateTime,
                PremiumSinceUtc = member.PremiumSince?.UtcDateTime,
                IsDeafened = member.IsDeafened == true,
                IsMuted = member.IsMuted == true,
                IsPending = member.IsPending == true,
                TimeoutUntilUtc = member.CommunicationDisabledUntil?.UtcDateTime
            },
            m => m.Id);
    }
}
