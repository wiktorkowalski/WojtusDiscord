using System.Text.Json;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public sealed class GuildMembersChunkHandler(EventPipeline pipeline) :
    IEventHandler<GuildMembersChunkedEventArgs>
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public async Task HandleEventAsync(DiscordClient sender, GuildMembersChunkedEventArgs args)
    {
        await pipeline.Execute(args, "GuildMembersChunked", nameof(GuildMembersChunkHandler),
            args.Guild.Id, null, null, async ctx =>
            {
                var userService = ctx.Services.GetRequiredService<UserService>();

                foreach (var member in args.Members)
                    await userService.UpsertUserAsync(member);

                ctx.Db.GuildMembersChunkEvents.Add(new GuildMembersChunkEventEntity
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
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();

                ctx.Logger.LogDebug("Recorded guild members chunk {ChunkIndex}/{ChunkCount} for guild {GuildId} with {MemberCount} members",
                    args.ChunkIndex, args.ChunkCount, args.Guild.Id, args.Members.Count);
            });
    }
}
