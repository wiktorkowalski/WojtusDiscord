using DSharpPlus;
using DSharpPlus.Net.Serialization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class AuditLogDispatchNullUserTests
{
    [Fact]
    public void AuditLogEntryCreate_WithNullUserId_Deserializes()
    {
        var transportType = typeof(DiscordClient).Assembly.GetType("DSharpPlus.Net.Abstractions.AuditLogAction", throwOnError: true)!;
        var payload = JObject.Parse("""
            {
              "guild_id": "742554855180206203",
              "id": "1562552190000000000",
              "action_type": 52,
              "target_id": "1562552177757655171",
              "user_id": null,
              "changes": []
            }
            """);

        // #376: nightly-02485 typed user_id as ulong, so this exact gateway dispatch step threw and dropped the event.
        var action = typeof(DiscordJson).GetMethod(nameof(DiscordJson.ToDiscordObject))!
            .MakeGenericMethod(transportType)
            .Invoke(null, [payload]);

        Assert.NotNull(action);
        Assert.Null(transportType.GetProperty("UserId")!.GetValue(action));
    }
}
