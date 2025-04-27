using System.Text;
using System.Text.Json;
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

    public async ValueTask HandleAsync(VoiceState voiceState)
    {
        var guild = gatewayClient.Cache.Guilds[voiceState.GuildId];

        IEnumerable<ulong>? usersInChannel = null;
        ulong? channelId = voiceState.ChannelId;
        if (channelId == null) // user leaves voice
        {
            channelId = guild.VoiceStates.Values
                                     .Where(vs => vs.UserId == voiceState.UserId)
                                     .Select(vs => vs.ChannelId)
                                     .First();
            usersInChannel = guild.VoiceStates.Values
                                     .Where(vs => vs.ChannelId == channelId && vs.UserId != voiceState.UserId)
                                     .Select(vs => vs.UserId);
        }
        else
        {
            // todo: cover case when user changes channels
            usersInChannel = guild.VoiceStates.Values
                                      .Where(vs => vs.ChannelId == voiceState.ChannelId)
                                      .Select(vs => vs.UserId)
                                      .Append(voiceState.UserId);
        }

        var channelStatus = new StringBuilder(string.Empty);
        foreach (var userIdInChannel in usersInChannel)
        {
            channelStatus.Append(_userEmoji.GetValueOrDefault(userIdInChannel, string.Empty));
        }

        var payload = new { status = channelStatus.ToString() };
        var jsonPayload = JsonSerializer.Serialize(payload);

        using HttpContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        await restClient.SendRequestAsync(HttpMethod.Put, content, $"/channels/{channelId}/voice-status");
    }
}
