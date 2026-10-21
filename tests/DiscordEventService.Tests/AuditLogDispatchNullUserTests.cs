using DSharpPlus;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DiscordEventService.Tests;

// #376: DSharpPlus 5.0.0-nightly-02485 read the audit log entry's user_id as a non-nullable ulong.
// A webhook deleted with its own token has no user, so GUILD_AUDIT_LOG_ENTRY_CREATE threw inside the
// gateway dispatch and the event was lost before any handler or raw log. The dispatch deserializes
// the payload into the internal AuditLogAction transport type; this repeats that step.
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

        var action = payload.ToObject(transportType, JsonSerializer.CreateDefault());

        Assert.NotNull(action);
        Assert.Null(transportType.GetProperty("UserId")!.GetValue(action));
    }
}
