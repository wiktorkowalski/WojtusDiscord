using System.Text;
using System.Text.Json;
using System.Net.Mime;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;

namespace EventListenerService.Handlers;

[GatewayEvent(nameof(GatewayClient.VoiceStateUpdate))]
public class VoiceStateUpdateHandler(ILogger<VoiceStateUpdateHandler> logger, RestClient restClient, GatewayClient gatewayClient) : IGatewayEventHandler<VoiceState>
{
    // this will eventually be moved to a database I guess
    private readonly Dictionary<ulong, string> _userEmoji = new()
    {
        { 172012484306141184, "<:biankaHead:1038959242754928743>" },
        { 170921674840080384, "<:heimkek:1038959260094185472>" },
        { 299247504481058817, "<:jamropointing:1232723835112128542>" }
    };

    private async ValueTask UpdateChannelStatus(ulong channelId, IEnumerable<ulong> usersInChannel)
    {
        var channelStatus = string.Join(string.Empty,
            usersInChannel.Select(userId => _userEmoji.GetValueOrDefault(userId, string.Empty)));

        var payload = new { status = channelStatus };
        var jsonPayload = JsonSerializer.Serialize(payload);

        using HttpContent content = new StringContent(jsonPayload, Encoding.UTF8, MediaTypeNames.Application.Json);
        try
        {
            await restClient.SendRequestAsync(HttpMethod.Put, content, $"/channels/{channelId}/voice-status");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update voice status for channel {ChannelId}.", channelId);
        }
    }

    public async ValueTask HandleAsync(VoiceState voiceState)
    {
        // Assumption: gatewayClient.Cache.Guilds[voiceState.GuildId].VoiceStates reflects the state *before* this specific update.
        // This assumption is crucial and might depend on NetCord's internal implementation.
        if (!gatewayClient.Cache.Guilds.TryGetValue(voiceState.GuildId, out var guild))
        {
            logger.LogWarning("Guild {GuildId} not found in cache for VoiceStateUpdate.", voiceState.GuildId);
            return;
        }

        // Determine the user's voice state *before* this event from the cache.
        guild.VoiceStates.TryGetValue(voiceState.UserId, out var cachedState);
        var previousChannelId = cachedState?.ChannelId; // Channel user was in before this event
        var currentChannelId = voiceState.ChannelId; // Channel user is in now (null if leaving)

        // --- Update the status of the channel the user LEFT (if any) ---
        // This happens if the user was in a channel before (previousChannelId != null)
        // AND they are now in a different channel OR no channel (currentChannelId != previousChannelId)
        if (previousChannelId is ulong oldChannelId && oldChannelId != currentChannelId)
        {
            // Get users remaining in the old channel (excluding the user who left/moved)
            // We query the cache which represents the state *before* the user left this channel.
            var usersRemainingInOldChannel = guild.VoiceStates.Values
                                                 .Where(vs => vs.ChannelId == oldChannelId && vs.UserId != voiceState.UserId)
                                                 .Select(vs => vs.UserId);
            await UpdateChannelStatus(oldChannelId, usersRemainingInOldChannel);
            logger.LogInformation("Updated status for channel {ChannelId} after user {UserId} left/moved.", oldChannelId, voiceState.UserId);
        }

        // --- Update the status of the channel the user JOINED (if any) ---
        // This happens if the user is in a channel now (currentChannelId != null)
        if (currentChannelId is ulong newChannelId)
        {
            // Get all users currently in the new channel.
            // This needs to reflect the state *after* the user joins/moves.
            // We combine the cached states of OTHERS in the new channel with the user's new state.

            // Start with OTHERS already in the new channel (from the cache state before the event)
            var otherUsersInNewChannel = guild.VoiceStates.Values
                                            .Where(vs => vs.ChannelId == newChannelId && vs.UserId != voiceState.UserId)
                                            .Select(vs => vs.UserId);

            // Add the current user (who just joined/moved into this channel)
            var allUsersInNewChannel = otherUsersInNewChannel.Append(voiceState.UserId);

            await UpdateChannelStatus(newChannelId, allUsersInNewChannel);
            logger.LogInformation("Updated status for channel {ChannelId} after user {UserId} joined/moved.", newChannelId, voiceState.UserId);
        }
    }
}
