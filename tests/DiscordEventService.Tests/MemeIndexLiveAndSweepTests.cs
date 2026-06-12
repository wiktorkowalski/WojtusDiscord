using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using DiscordEventService.Services.MemeIndexing;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class MemeIndexLiveAndSweepTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 1UL;
    private const ulong ChannelDiscordId = 2UL;
    private const ulong OtherChannelDiscordId = 99UL;

    private DiscordDbContext _db = null!;
    private GuildEntity _guild = null!;
    private ChannelEntity _channel = null!;
    private ChannelEntity _otherChannel = null!;
    private UserEntity _author = null!;
    private FakeMemeHttpHandler _http = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();

        await _db.MemeIndex.ExecuteDeleteAsync();
        await _db.BackfillCheckpoints.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        _guild = new GuildEntity { DiscordId = GuildDiscordId, Name = "g" };
        _db.Guilds.Add(_guild);
        await _db.SaveChangesAsync();

        _channel = new ChannelEntity { DiscordId = ChannelDiscordId, GuildId = _guild.Id, Name = "memes", Type = ChannelType.Text };
        _otherChannel = new ChannelEntity { DiscordId = OtherChannelDiscordId, GuildId = _guild.Id, Name = "general", Type = ChannelType.Text };
        _author = new UserEntity { DiscordId = 3UL, Username = "u" };
        _db.Channels.AddRange(_channel, _otherChannel);
        _db.Users.Add(_author);
        await _db.SaveChangesAsync();

        _http = new FakeMemeHttpHandler();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task IndexMessageAsync_IndexesOnlyThatMessage_AndCreatesNoCheckpoint()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "fresh.png"), Attachment(12UL, "also-fresh.png"));
        AddMessage(1002UL, _channel, Attachment(13UL, "someone-elses.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.SetImage(13UL, Png(3));

        await RunLiveAsync(1001UL);

        await using var verify = NewContext();
        var rows = await verify.MemeIndex.OrderBy(m => m.AttachmentDiscordId).ToListAsync();
        Assert.Equal([11UL, 12UL], rows.Select(r => r.AttachmentDiscordId));
        Assert.All(rows, r => Assert.Equal(MemeIndexStatus.Indexed, r.Status));
        Assert.Equal(2, _http.ModelCalls);

        // Live runs never touch the backfill/sweep checkpoint machinery.
        Assert.Equal(0, await verify.BackfillCheckpoints.CountAsync());
    }

    [Fact]
    public async Task IndexMessageAsync_Rerun_MakesNoExtraModelCalls()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunLiveAsync(1001UL);
        Assert.Equal(1, _http.ModelCalls);

        // A Hangfire retry of the same job must be a no-op on terminal rows.
        await RunLiveAsync(1001UL);

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        Assert.Equal(1, await verify.MemeIndex.CountAsync());
    }

    [Fact]
    public async Task IndexMessageAsync_MessageOutsideMemeChannels_IsIgnored()
    {
        AddMessage(1001UL, _otherChannel, Attachment(11UL, "not-a-meme.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunLiveAsync(1001UL);

        Assert.Equal(0, _http.ModelCalls);
        await using var verify = NewContext();
        Assert.Equal(0, await verify.MemeIndex.CountAsync());
    }

    [Fact]
    public async Task ExecuteSweepAsync_IndexesAttachmentsMissedDuringDowntime()
    {
        // Messages landed in the DB (e.g. via backfill after downtime) but the
        // live path never fired — no meme_index rows exist.
        AddMessage(1001UL, _channel, Attachment(11UL, "missed-1.png"));
        AddMessage(1002UL, _channel, Attachment(12UL, "missed-2.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));

        await RunSweepAsync();

        await using var verify = NewContext();
        var rows = await verify.MemeIndex.OrderBy(m => m.AttachmentDiscordId).ToListAsync();
        Assert.Equal([11UL, 12UL], rows.Select(r => r.AttachmentDiscordId));
        Assert.All(rows, r => Assert.Equal(MemeIndexStatus.Indexed, r.Status));

        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(BackfillStatus.Completed, checkpoint.Status);
    }

    [Fact]
    public async Task ExecuteSweepAsync_RetriesFailedUnderCap_LeavesCappedFailedAndSkippedUntouched()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "retry-me.png"));
        AddMessage(1002UL, _channel, Attachment(12UL, "given-up.png"));
        AddMessage(1003UL, _channel, Attachment(13UL, "refused-before.png"));
        await _db.SaveChangesAsync();
        SeedRow(1001UL, 11UL, "retry-me.png", MemeIndexStatus.Failed, attemptCount: 1);
        SeedRow(1002UL, 12UL, "given-up.png", MemeIndexStatus.Failed, attemptCount: MemeIndexingJob.SweepMaxFailedAttempts);
        SeedRow(1003UL, 13UL, "refused-before.png", MemeIndexStatus.Skipped, attemptCount: 1);
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunSweepAsync();

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        var byId = await verify.MemeIndex.ToDictionaryAsync(m => m.AttachmentDiscordId);
        Assert.Equal(MemeIndexStatus.Indexed, byId[11UL].Status);
        Assert.Equal(2, byId[11UL].AttemptCount);
        Assert.Equal(MemeIndexStatus.Failed, byId[12UL].Status);
        Assert.Equal(MemeIndexingJob.SweepMaxFailedAttempts, byId[12UL].AttemptCount);
        Assert.Equal(MemeIndexStatus.Skipped, byId[13UL].Status);
        Assert.Equal(1, byId[13UL].AttemptCount);
    }

    [Fact]
    public async Task ExecuteSweepAsync_CleanRun_MakesNoModelOrCdnCalls()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunSweepAsync();
        Assert.Equal(1, _http.ModelCalls);

        await RunSweepAsync();

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync();
        Assert.Equal(1, row.AttemptCount);
    }

    [Fact]
    public async Task ExecuteAsync_ConfiguredGuilds_EnqueuesOnePerGuildJob()
    {
        var jobClient = new RecordingJobClient();

        await RunCoordinatorAsync(jobClient, configured: true);

        var job = Assert.Single(jobClient.Created);
        Assert.Equal(nameof(MemeIndexingJob.ExecuteSweepAsync), job.Method.Name);
        Assert.Equal(GuildDiscordId, job.Args[0]);
    }

    [Fact]
    public async Task ExecuteAsync_GuildWithIndexingInProgress_IsSkipped()
    {
        _db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildDiscordId,
            Type = BackfillType.MemeIndex,
            Status = BackfillStatus.InProgress,
            StartedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        var jobClient = new RecordingJobClient();

        await RunCoordinatorAsync(jobClient, configured: true);

        Assert.Empty(jobClient.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Unconfigured_IsANoOp()
    {
        var jobClient = new RecordingJobClient();

        await RunCoordinatorAsync(jobClient, configured: false);

        Assert.Empty(jobClient.Created);
    }

    private async Task RunLiveAsync(ulong messageDiscordId)
    {
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MemeIndexingJob>()
            .IndexMessageAsync(GuildDiscordId, messageDiscordId, CancellationToken.None);
    }

    private async Task RunSweepAsync()
    {
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MemeIndexingJob>()
            .ExecuteSweepAsync(GuildDiscordId, CancellationToken.None);
    }

    private async Task RunCoordinatorAsync(RecordingJobClient jobClient, bool configured)
    {
        await using var provider = BuildProvider(configured ? [ChannelDiscordId] : [], jobClient);
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MemeIndexSweepJob>().ExecuteAsync(CancellationToken.None);
    }

    private ServiceProvider BuildProvider(ulong[]? channelIds = null, IBackgroundJobClient? jobClient = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DiscordDbContext>(o => o
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention());
        services.Configure<MemeIndexOptions>(o => o.ChannelIds = channelIds ?? [ChannelDiscordId]);
        services.Configure<OpenRouterOptions>(o =>
        {
            o.ApiKey = "test-key";
            o.Model = "test/model";
            o.RequestDelayMs = 0;
        });
        services.Configure<DiscordOptions>(o => o.Token = new string('x', 60));
        services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(_http));
        services.AddSingleton(jobClient ?? new RecordingJobClient());
        services.AddScoped<MemeSampleService>();
        services.AddScoped<AttachmentUrlRefreshService>();
        services.AddScoped<OpenRouterClient>();
        services.AddScoped<MemeAttachmentIndexer>();
        services.AddScoped<BackfillJobExecutor>();
        services.AddScoped<MemeIndexingJob>();
        services.AddScoped<MemeIndexSweepJob>();
        return services.BuildServiceProvider();
    }

    private void AddMessage(ulong discordId, ChannelEntity channel, params string[] attachments)
    {
        _db.Messages.Add(new MessageEntity
        {
            DiscordId = discordId,
            ChannelId = channel.Id,
            GuildId = _guild.Id,
            AuthorId = _author.Id,
            HasAttachments = true,
            AttachmentsJson = $"[{string.Join(",", attachments)}]",
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private void SeedRow(ulong messageDiscordId, ulong attachmentId, string fileName, MemeIndexStatus status, int attemptCount)
    {
        _db.MemeIndex.Add(new MemeIndexEntity
        {
            MessageId = _db.Messages.Local.Single(m => m.DiscordId == messageDiscordId).Id,
            GuildDiscordId = GuildDiscordId,
            ChannelDiscordId = ChannelDiscordId,
            MessageDiscordId = messageDiscordId,
            AttachmentDiscordId = attachmentId,
            FileName = fileName,
            FileSizeBytes = 123,
            Status = status,
            Error = "seeded by test",
            AttemptCount = attemptCount
        });
    }

    // The 4-field PascalCase shape MessageEventHandler/MessagesBackfillJob serialize.
    private static string Attachment(ulong id, string fileName) =>
        $"{{\"Id\":{id},\"Url\":\"https://cdn.test/attachments/{ChannelDiscordId}/{id}/{fileName}?ex=expired\",\"FileName\":\"{fileName}\",\"FileSize\":123}}";

    // Distinct valid-PNG-magic payloads (≥12 bytes for the sniffer).
    private static byte[] Png(byte seed) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, seed, seed, seed];

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}

// Captures enqueues without a storage backend.
internal sealed class RecordingJobClient : IBackgroundJobClient
{
    public List<Job> Created { get; } = [];

    public string Create(Job job, IState state)
    {
        Created.Add(job);
        return Created.Count.ToString();
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}
