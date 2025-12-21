using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

public class UserService(DiscordDbContext db, ILogger<UserService> logger)
{
    public async Task UpsertUserAsync(DiscordUser user)
    {
        var rowsAffected = await db.Users
            .Where(u => u.DiscordId == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Username, user.Username)
                .SetProperty(u => u.GlobalName, user.GlobalName)
                .SetProperty(u => u.Discriminator, user.Discriminator)
                .SetProperty(u => u.AvatarHash, user.AvatarHash));

        if (rowsAffected == 0)
        {
            try
            {
                db.Users.Add(new UserEntity
                {
                    DiscordId = user.Id,
                    Username = user.Username,
                    GlobalName = user.GlobalName,
                    Discriminator = user.Discriminator,
                    AvatarHash = user.AvatarHash,
                    IsBot = user.IsBot,
                    IsSystem = user.IsSystem ?? false
                });
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // Unique constraint violation - race condition, another request inserted first
                db.ChangeTracker.Clear();
                await db.Users
                    .Where(u => u.DiscordId == user.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.Username, user.Username)
                        .SetProperty(u => u.GlobalName, user.GlobalName)
                        .SetProperty(u => u.Discriminator, user.Discriminator)
                        .SetProperty(u => u.AvatarHash, user.AvatarHash));
            }
        }
    }

    public async Task UpsertMemberAsync(DiscordMember member)
    {
        await UpsertUserAsync(member);

        // Get the User and Guild Guids
        var userEntity = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == member.Id);
        var guildEntity = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == member.Guild.Id);

        if (userEntity is null || guildEntity is null)
        {
            logger.LogWarning("Cannot upsert member: User={UserFound} Guild={GuildFound} for MemberId={MemberId} GuildId={GuildId}",
                userEntity != null, guildEntity != null, member.Id, member.Guild.Id);
            return;
        }

        var rowsAffected = await db.Members
            .Where(m => m.UserId == userEntity.Id && m.GuildId == guildEntity.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Nickname, member.Nickname)
                .SetProperty(m => m.GuildAvatarHash, member.GuildAvatarHash)
                .SetProperty(m => m.PremiumSinceUtc, member.PremiumSince != null ? member.PremiumSince.Value.UtcDateTime : (DateTime?)null)
                .SetProperty(m => m.IsDeafened, member.IsDeafened == true)
                .SetProperty(m => m.IsMuted, member.IsMuted == true)
                .SetProperty(m => m.IsPending, member.IsPending == true)
                .SetProperty(m => m.TimeoutUntilUtc, member.CommunicationDisabledUntil != null ? member.CommunicationDisabledUntil.Value.UtcDateTime : (DateTime?)null));

        if (rowsAffected == 0)
        {
            try
            {
                db.Members.Add(new MemberEntity
                {
                    UserId = userEntity.Id,
                    GuildId = guildEntity.Id,
                    Nickname = member.Nickname,
                    GuildAvatarHash = member.GuildAvatarHash,
                    JoinedAtUtc = member.JoinedAt.UtcDateTime,
                    PremiumSinceUtc = member.PremiumSince?.UtcDateTime,
                    IsDeafened = member.IsDeafened == true,
                    IsMuted = member.IsMuted == true,
                    IsPending = member.IsPending == true,
                    TimeoutUntilUtc = member.CommunicationDisabledUntil?.UtcDateTime
                });
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // Unique constraint violation - race condition, another request inserted first
                db.ChangeTracker.Clear();
                await db.Members
                    .Where(m => m.UserId == userEntity.Id && m.GuildId == guildEntity.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.Nickname, member.Nickname)
                        .SetProperty(m => m.GuildAvatarHash, member.GuildAvatarHash)
                        .SetProperty(m => m.PremiumSinceUtc, member.PremiumSince != null ? member.PremiumSince.Value.UtcDateTime : (DateTime?)null)
                        .SetProperty(m => m.IsDeafened, member.IsDeafened == true)
                        .SetProperty(m => m.IsMuted, member.IsMuted == true)
                        .SetProperty(m => m.IsPending, member.IsPending == true)
                        .SetProperty(m => m.TimeoutUntilUtc, member.CommunicationDisabledUntil != null ? member.CommunicationDisabledUntil.Value.UtcDateTime : (DateTime?)null));
            }
        }
    }
}
