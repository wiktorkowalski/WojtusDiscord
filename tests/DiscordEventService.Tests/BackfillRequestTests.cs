using DiscordEventService.Endpoints;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class BackfillRequestTests
{
    [Fact]
    public void AfterUtc_Null_StaysNull() =>
        Assert.Null(new BackfillRequest().AfterUtc());

    [Fact]
    public void AfterUtc_Unspecified_IsTakenAsUtc()
    {
        var after = new BackfillRequest { After = new DateTime(2026, 8, 16, 10, 0, 0, DateTimeKind.Unspecified) }.AfterUtc();

        Assert.Equal(DateTimeKind.Utc, after!.Value.Kind);
        Assert.Equal(new DateTime(2026, 8, 16, 10, 0, 0), after.Value);
    }

    [Fact]
    public void AfterUtc_Local_IsConverted()
    {
        var local = new DateTime(2026, 8, 16, 10, 0, 0, DateTimeKind.Local);

        var after = new BackfillRequest { After = local }.AfterUtc();

        Assert.Equal(DateTimeKind.Utc, after!.Value.Kind);
        Assert.Equal(local.ToUniversalTime(), after.Value);
    }
}
