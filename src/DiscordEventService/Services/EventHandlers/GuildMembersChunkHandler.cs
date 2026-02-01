using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;
using System.Text.Json;

namespace DiscordEventService.Services.EventHandlers;

public class GuildMembersChunkHandler(IServiceScopeFactory scopeFactory, ILogger<GuildMembersChunkHandler> logger) :
    IEventHandler<GuildMembersChunkedEventArgs>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task HandleEventAsync(DiscordClient sender, GuildMembersChunkedEventArgs args)
    {
        try
        {
            var now = DateTime.UtcNow;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                args, "GuildMembersChunked", args.Guild.Id, null, null);

            // Upsert all members received in this chunk
            foreach (var member in args.Members)
            {
                await userService.UpsertUserAsync(member);
            }

            var chunkEvent = new GuildMembersChunkEventEntity
            {
                GuildDiscordId = args.Guild.Id,
                ChunkIndex = args.ChunkIndex,
                ChunkCount = args.ChunkCount,
                MemberCount = args.Members.Count,
                MemberIdsJson = JsonSerializer.Serialize(args.Members.Select(m => m.Id.ToString()), JsonOptions),
                PresencesJson = args.Presences?.Count > 0
                    ? JsonSerializer.Serialize(args.Presences.Select(p => new
                    {
                        UserId = p.User.Id.ToString(),
                        Status = p.Status.ToString(),
                        Activities = p.Activities?.Select(a => new { a.Name, Type = (int)a.ActivityType })
                    }), JsonOptions)
                    : null,
                Nonce = args.Nonce,
                NotFoundIdsJson = args.NotFound?.Count > 0
                    ? JsonSerializer.Serialize(args.NotFound.Select(id => id.ToString()), JsonOptions)
                    : null,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            await db.GuildMembersChunkEvents.AddAsync(chunkEvent);
            await db.SaveChangesAsync();

            logger.LogDebug("Recorded guild members chunk {ChunkIndex}/{ChunkCount} for guild {GuildId} with {MemberCount} members",
                args.ChunkIndex, args.ChunkCount, args.Guild.Id, args.Members.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling guild members chunk for guild {GuildId}", args.Guild.Id);
        }
    }
}
