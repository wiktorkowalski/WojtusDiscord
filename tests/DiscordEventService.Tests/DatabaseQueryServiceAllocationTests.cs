using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Services.Conversation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// GC.GetTotalAllocatedBytes counts the whole process, so this test runs alone: other test classes
// running in parallel allocate hundreds of MB and hide the signal.
[CollectionDefinition("ProcessAllocations", DisableParallelization = true)]
public sealed class ProcessAllocationsCollection;

[Collection("ProcessAllocations")]
public sealed class DatabaseQueryServiceAllocationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task RunAsync_HugeText_IsReadOnlyUpToTheFieldCap()
    {
        // 100M chars is 200 MB as a .NET string; a bounded read allocates a few KB of it.
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var (result, failed) = await new DatabaseQueryService(
            context, Options.Create(new ConversationOptions()), NullLogger<DatabaseQueryService>.Instance).RunAsync(
            "SELECT repeat('x', 100000000) AS v, decode(repeat('ab', 50000000), 'hex') AS b, 1 AS after",
            CancellationToken.None);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.False(failed, result);
        Assert.True(allocated < 20_000_000, $"allocated {allocated} bytes");
        using var doc = JsonDocument.Parse(result);
        var row = doc.RootElement.GetProperty("rows")[0];
        Assert.Equal(DatabaseQueryService.MaxFieldLength + DatabaseQueryService.TruncationMarker.Length, row.GetProperty("v").GetString()!.Length);
        Assert.EndsWith(DatabaseQueryService.TruncationMarker, row.GetProperty("b").GetString());
        // The column after the cut ones still reads: Npgsql skipped the rest of each value.
        Assert.Equal(1, row.GetProperty("after").GetInt32());
    }

    private DiscordDbContext NewContext() =>
        new(new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}
