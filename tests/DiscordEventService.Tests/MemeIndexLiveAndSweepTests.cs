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
    private const string ConfiguredModel = "test/model";

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

        await _db.MemeAnnotations.ExecuteDeleteAsync();
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
    public async Task IndexMessageAsync_WritesOneAnnotationPerImage()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "fresh.png"), Attachment(12UL, "also-fresh.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));

        await RunLiveAsync(1001UL);

        await using var verify = NewContext();
        await AssertConfiguredWriterAnnotationsAsync(verify, 11UL, 12UL);
    }

    [Fact]
    public async Task IndexMessageAsync_StoresEverySchemaV2FieldOfTheModelOutput()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "fresh.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunLiveAsync(1001UL);

        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        Assert.Equal(OpenRouterClient.PromptVersion, annotation.PromptVersion);
        StoredMemeOutput.AssertDefaultOutput(annotation);
        StoredMemeOutput.AssertVerbatimRawResponse(annotation, _http.MetadataJsonFor(Png(1)));
    }

    // The acceptance criterion of #368, through the live hook: one leaky writer is enough to
    // bring a guessed name back into search.
    [Fact]
    public async Task IndexMessageAsync_CutoutThatNamesAPerson_IsStoredWithoutThePerson()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.CutoutFor.Add(Png(1));

        await RunLiveAsync(1001UL);

        await using var verify = NewContext();
        Assert.Equal(MemeIndexStatus.Indexed, (await verify.MemeIndex.SingleAsync()).Status);
        StoredMemeOutput.AssertCutoutWithoutThePerson(await verify.MemeAnnotations.SingleAsync());
    }

    [Fact]
    public async Task IndexMessageAsync_CutoutThatNamesAPerson_StoresARawResponseWithoutTheName()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.CutoutFor.Add(Png(1));

        await RunLiveAsync(1001UL);

        await using var verify = NewContext();
        StoredMemeOutput.AssertRawResponseWithoutTheName(await verify.MemeAnnotations.SingleAsync());
    }

    // The control: the same output, only the image kind differs.
    [Fact]
    public async Task IndexMessageAsync_NamedPersonOnAnotherImageKind_KeepsThePersonTheTagsAndTheVerbatimRawResponse()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "photo.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.NamedPersonFor.Add(Png(1));
        _http.Overrides.Add((Png(1), "source", null));

        await RunLiveAsync(1001UL);

        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        StoredMemeOutput.AssertNamedPersonKept(annotation);
        StoredMemeOutput.AssertVerbatimRawResponse(annotation, _http.MetadataJsonFor(Png(1)));
    }

    // The sweep is the third way in. It shares the indexer, and this keeps it that way.
    [Fact]
    public async Task ExecuteSweepAsync_CutoutThatNamesAPerson_IsStoredWithoutThePerson()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.CutoutFor.Add(Png(1));

        await RunSweepAsync();

        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        StoredMemeOutput.AssertCutoutWithoutThePerson(annotation);
        StoredMemeOutput.AssertRawResponseWithoutTheName(annotation);
    }

    [Fact]
    public async Task IndexMessageAsync_Rerun_KeepsOneAnnotationPerKey()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunLiveAsync(1001UL);
        await RunLiveAsync(1001UL);

        await using var verify = NewContext();
        await AssertConfiguredWriterAnnotationsAsync(verify, 11UL);
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

    // The live hook looks at the status only: an Indexed attachment is done, even when the
    // configured writer has not annotated it. Only the manual backfill pays for that (#367).
    [Fact]
    public async Task IndexMessageAsync_IndexedRowWithoutConfiguredKeyAnnotation_IsNotRevisited()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _db.MemeIndex.Add(new MemeIndexEntity
        {
            MessageId = _db.Messages.Local.Single(m => m.DiscordId == 1001UL).Id,
            GuildDiscordId = GuildDiscordId,
            ChannelDiscordId = ChannelDiscordId,
            MessageDiscordId = 1001UL,
            AttachmentDiscordId = 11UL,
            FileName = "a.png",
            FileSizeBytes = 123,
            Status = MemeIndexStatus.Indexed,
            AttemptCount = 1,
            Annotations =
            [
                new MemeAnnotationEntity
                {
                    AttachmentDiscordId = 11UL,
                    ModelId = "other/model",
                    PromptVersion = OpenRouterClient.PromptVersion,
                    IndexedAtUtc = DateTime.UtcNow,
                    DescriptionPl = "Opis innego modelu",
                    DescriptionEn = "Another model's description",
                    OcrText = "",
                    Tags = ["seed"],
                },
            ],
        });
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunLiveAsync(1001UL);

        Assert.Equal(0, _http.ModelCalls);
        Assert.Equal(0, _http.CdnRequests);
        await using var verify = NewContext();
        Assert.Equal("other/model", (await verify.MemeAnnotations.SingleAsync()).ModelId);
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

    // #374: a save that Postgres rejects (NUL byte) must not leave the job. An exception here makes
    // Hangfire retry the whole job, and every retry pays for the model call again.
    [Fact]
    public async Task IndexMessageAsync_PoisonAttachment_LandsFailedOnce_AndTheOtherAttachmentsAreIndexed()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "fine.png"), Attachment(12UL, "nul-byte.png"), Attachment(13UL, "also-fine.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.SetImage(13UL, Png(3));
        _http.NulOcrFor.Add(Png(2));

        await RunLiveAsync(1001UL);

        // One model call per attachment in this job, the poisoned one included.
        Assert.Equal(3, _http.ModelCalls);
        await using var verify = NewContext();
        var rows = await verify.MemeIndex.OrderBy(m => m.AttachmentDiscordId).ToListAsync();
        Assert.Equal([11UL, 12UL, 13UL], rows.Select(r => r.AttachmentDiscordId));
        Assert.Equal(MemeIndexStatus.Indexed, rows[0].Status);
        Assert.Equal(MemeIndexStatus.Failed, rows[1].Status);
        Assert.StartsWith("poisoned: ", rows[1].Error);
        Assert.Equal(1, rows[1].AttemptCount);
        Assert.Equal(MemeIndexStatus.Indexed, rows[2].Status);
        // The rejected annotation went down with its save; the neighbours kept theirs.
        await AssertConfiguredWriterAnnotationsAsync(verify, 11UL, 13UL);
    }

    // #374: a concurrent run (sweep, manual backfill) lands the attachment while this job waits for
    // the model. This job's insert then hits the unique index; the winner's row must stand.
    [Fact]
    public async Task IndexMessageAsync_ConcurrentRunIndexesSameAttachment_RecoveryKeepsItsIndexedRow()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "raced.png"), Attachment(12UL, "fine.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        var messageId = _db.Messages.Local.Single(m => m.DiscordId == 1001UL).Id;

        var raced = false;
        _http.DuringModelCall = async () =>
        {
            if (raced)
                return;
            raced = true;
            await using var other = NewContext();
            other.MemeIndex.Add(new MemeIndexEntity
            {
                MessageId = messageId,
                GuildDiscordId = GuildDiscordId,
                ChannelDiscordId = ChannelDiscordId,
                MessageDiscordId = 1001UL,
                AttachmentDiscordId = 11UL,
                FileName = "raced.png",
                FileSizeBytes = 123,
                Status = MemeIndexStatus.Indexed,
                AttemptCount = 1,
                Annotations =
                [
                    new MemeAnnotationEntity
                    {
                        AttachmentDiscordId = 11UL,
                        ModelId = "other/run",
                        PromptVersion = OpenRouterClient.PromptVersion,
                        IndexedAtUtc = DateTime.UtcNow,
                        DescriptionPl = "wygrany",
                        DescriptionEn = "winner",
                        OcrText = "",
                        RawResponseJson = "{}",
                    },
                ],
            });
            await other.SaveChangesAsync();
        };

        await RunLiveAsync(1001UL);

        Assert.Equal(2, _http.ModelCalls);
        await using var verify = NewContext();
        var byId = await verify.MemeIndex.ToDictionaryAsync(m => m.AttachmentDiscordId);
        Assert.Equal(MemeIndexStatus.Indexed, byId[11UL].Status);
        Assert.Null(byId[11UL].Error);
        Assert.Equal(1, byId[11UL].AttemptCount);
        // This run's own annotation was in the rejected save; the winner's row and annotation stand.
        Assert.Equal("other/run", (await verify.MemeAnnotations.SingleAsync(a => a.AttachmentDiscordId == 11UL)).ModelId);
        Assert.Equal(MemeIndexStatus.Indexed, byId[12UL].Status);
        Assert.Equal(ConfiguredModel, (await verify.MemeAnnotations.SingleAsync(a => a.AttachmentDiscordId == 12UL)).ModelId);
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
    public async Task ExecuteSweepAsync_WritesAnnotationsForAttachmentsMissedDuringDowntime()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "missed-1.png"));
        AddMessage(1002UL, _channel, Attachment(12UL, "missed-2.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));

        await RunSweepAsync();

        await using var verify = NewContext();
        await AssertConfiguredWriterAnnotationsAsync(verify, 11UL, 12UL);
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

        // #312: the checkpoint carries the job id so cancel / the startup sweep can delete the job.
        await using var verify = NewContext();
        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(BackfillStatus.Pending, checkpoint.Status);
        Assert.Equal("1", checkpoint.HangfireJobId);
    }

    [Fact]
    public async Task ExecuteAsync_GuildWithFreshPendingCheckpoint_IsSkipped()
    {
        // Enqueued but not started yet (#312) — must not get a second job (#289).
        _db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildDiscordId,
            Type = BackfillType.MemeIndex,
            Status = BackfillStatus.Pending,
            HangfireJobId = "queued-earlier",
            StartedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        var jobClient = new RecordingJobClient();

        await RunCoordinatorAsync(jobClient, configured: true);

        Assert.Empty(jobClient.Created);
    }

    [Fact]
    public async Task Enqueuer_FailedCheckpoint_BecomesPendingWithJobId()
    {
        var completedAt = DateTime.UtcNow.AddHours(-2);
        _db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildDiscordId,
            Type = BackfillType.MemeIndex,
            Status = BackfillStatus.Failed,
            HangfireJobId = "old-job",
            StartedAtUtc = completedAt.AddMinutes(-30),
            CompletedAtUtc = completedAt
        });
        await _db.SaveChangesAsync();
        var jobClient = new RecordingJobClient();

        await using var db = NewContext();
        var jobId = await MemeIndexJobEnqueuer.EnqueueAsync(db, jobClient, GuildDiscordId, sweep: false, CancellationToken.None);

        Assert.Equal("1", jobId);
        Assert.Equal(nameof(MemeIndexingJob.ExecuteAsync), Assert.Single(jobClient.Created).Method.Name);
        await using var verify = NewContext();
        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(BackfillStatus.Pending, checkpoint.Status);
        Assert.Equal("1", checkpoint.HangfireJobId);
        Assert.Null(checkpoint.CompletedAtUtc);
        Assert.True(checkpoint.StartedAtUtc > completedAt);
    }

    [Fact]
    public async Task Enqueuer_OldJobId_IsClearedBeforeTheJobExists()
    {
        _db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildDiscordId,
            Type = BackfillType.MemeIndex,
            Status = BackfillStatus.Completed,
            HangfireJobId = "old-job",
            StartedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        // Observes the row at the moment the job is created, i.e. between the two saves.
        var jobClient = new SnapshottingJobClient(NewContext, GuildDiscordId);

        await using var db = NewContext();
        await MemeIndexJobEnqueuer.EnqueueAsync(db, jobClient, GuildDiscordId, sweep: false, CancellationToken.None);

        Assert.Equal(BackfillStatus.Pending, jobClient.StatusAtCreate);
        Assert.Null(jobClient.JobIdAtCreate);
    }

    [Fact]
    public async Task Enqueuer_IdSave_LandsEvenWhenTheTokenIsAlreadyCancelled()
    {
        // Shutdown between enqueue and the id save (the sweep runs under Hangfire's shutdown token):
        // the job exists in storage, so its id must reach the row or it re-runs orphaned.
        var jobClient = new RecordingJobClient();
        using var cts = new CancellationTokenSource();
        var cancelOnCreate = new CancelOnCreateJobClient(jobClient, cts);

        await using var db = NewContext();
        await MemeIndexJobEnqueuer.EnqueueAsync(db, cancelOnCreate, GuildDiscordId, sweep: true, cts.Token);

        await using var verify = NewContext();
        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal("1", checkpoint.HangfireJobId);
    }

    [Fact]
    public async Task Enqueuer_StaleInProgressCheckpoint_KeepsStatusAndCursor_GetsJobId()
    {
        // A dead job's row (#293): the executor must still see InProgress to resume from the cursor.
        _db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildDiscordId,
            Type = BackfillType.MemeIndex,
            Status = BackfillStatus.InProgress,
            LastProcessedId = 1001UL,
            ProcessedCount = 7,
            StartedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        var jobClient = new RecordingJobClient();

        await using var db = NewContext();
        await MemeIndexJobEnqueuer.EnqueueAsync(db, jobClient, GuildDiscordId, sweep: true, CancellationToken.None);

        await using var verify = NewContext();
        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(BackfillStatus.InProgress, checkpoint.Status);
        Assert.Equal(1001UL, checkpoint.LastProcessedId);
        Assert.Equal(7, checkpoint.ProcessedCount);
        Assert.Equal("1", checkpoint.HangfireJobId);
    }

    [Fact]
    public async Task ExecuteSweepAsync_AfterEnqueue_CompletesAndKeepsJobId()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        var jobClient = new RecordingJobClient();
        await using (var db = NewContext())
            await MemeIndexJobEnqueuer.EnqueueAsync(db, jobClient, GuildDiscordId, sweep: true, CancellationToken.None);

        // What Hangfire would do next: run the job the enqueuer just recorded.
        await RunSweepAsync();

        await using var verify = NewContext();
        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(BackfillStatus.Completed, checkpoint.Status);
        Assert.Equal("1", checkpoint.HangfireJobId);
        Assert.Equal(1, _http.ModelCalls);
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
    public async Task ExecuteAsync_GuildWithStaleInProgressCheckpoint_IsSwept()
    {
        _db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildDiscordId,
            Type = BackfillType.MemeIndex,
            Status = BackfillStatus.InProgress,
            StartedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        // ITimestamped bumps LastUpdatedUtc on every SaveChanges; ExecuteUpdate bypasses it so the
        // heartbeat can be aged past the staleness cutoff (#293: dead job must not block the sweep).
        await _db.BackfillCheckpoints
            .Where(c => c.GuildDiscordId == GuildDiscordId && c.Type == BackfillType.MemeIndex)
            .ExecuteUpdateAsync(s => s.SetProperty(
                c => c.LastUpdatedUtc,
                DateTime.UtcNow - BackfillCheckpointEntity.StaleInProgressAfter - TimeSpan.FromMinutes(1)));
        var jobClient = new RecordingJobClient();

        await RunCoordinatorAsync(jobClient, configured: true);

        var job = Assert.Single(jobClient.Created);
        Assert.Equal(nameof(MemeIndexingJob.ExecuteSweepAsync), job.Method.Name);
    }

    [Fact]
    public async Task ExecuteAsync_Unconfigured_IsANoOp()
    {
        var jobClient = new RecordingJobClient();

        await RunCoordinatorAsync(jobClient, configured: false);

        Assert.Empty(jobClient.Created);
    }

    // #369: ChannelIds plus an OpenRouter key must not start spending. The import needs
    // ChannelIds too, so the two automatic paths have their own switch, and it is off by default.
    [Fact]
    public void AutomaticIndexing_Default_IsOff()
    {
        Assert.False(new MemeIndexOptions().AutomaticIndexing);
    }

    [Fact]
    public async Task ExecuteAsync_AutomaticIndexingOff_EnqueuesNothing()
    {
        var jobClient = new RecordingJobClient();

        await RunCoordinatorAsync(jobClient, configured: true, automaticIndexing: false);

        Assert.Empty(jobClient.Created);
        await using var verify = NewContext();
        Assert.Equal(0, await verify.BackfillCheckpoints.CountAsync());
    }

    [Fact]
    public async Task IndexMessageAsync_AutomaticIndexingOff_MakesNoDownloadNoModelCallAndWritesNoRow()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "fresh.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunLiveAsync(1001UL, automaticIndexing: false);

        Assert.Equal(0, _http.ModelCalls);
        Assert.Equal(0, _http.CdnRequests);
        await using var verify = NewContext();
        Assert.Equal(0, await verify.MemeIndex.CountAsync());
        Assert.Equal(0, await verify.MemeAnnotations.CountAsync());
    }

    // A sweep job enqueued before a restart that turned the switch off: the job itself checks too.
    [Fact]
    public async Task ExecuteSweepAsync_AutomaticIndexingOff_MakesNoDownloadNoModelCallAndWritesNoRow()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "missed.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunSweepAsync(automaticIndexing: false);

        Assert.Equal(0, _http.ModelCalls);
        Assert.Equal(0, _http.CdnRequests);
        await using var verify = NewContext();
        Assert.Equal(0, await verify.MemeIndex.CountAsync());
        Assert.Equal(0, await verify.MemeAnnotations.CountAsync());
    }

    // The manual backfill is a human trigger already: it does not read the switch.
    [Fact]
    public async Task ExecuteAsync_ManualBackfillWithAutomaticIndexingOff_StillIndexes()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunBackfillAsync(automaticIndexing: false);

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        Assert.Equal(MemeIndexStatus.Indexed, (await verify.MemeIndex.SingleAsync()).Status);
        await AssertConfiguredWriterAnnotationsAsync(verify, 11UL);
    }

    private async Task RunLiveAsync(ulong messageDiscordId, bool automaticIndexing = true)
    {
        await using var provider = BuildProvider(automaticIndexing: automaticIndexing);
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MemeIndexingJob>()
            .IndexMessageAsync(GuildDiscordId, messageDiscordId, CancellationToken.None);
    }

    private async Task RunSweepAsync(bool automaticIndexing = true)
    {
        await using var provider = BuildProvider(automaticIndexing: automaticIndexing);
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MemeIndexingJob>()
            .ExecuteSweepAsync(GuildDiscordId, CancellationToken.None);
    }

    private async Task RunBackfillAsync(bool automaticIndexing)
    {
        await using var provider = BuildProvider(automaticIndexing: automaticIndexing);
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MemeIndexingJob>()
            .ExecuteAsync(GuildDiscordId, CancellationToken.None);
    }

    private async Task RunCoordinatorAsync(RecordingJobClient jobClient, bool configured, bool automaticIndexing = true)
    {
        await using var provider = BuildProvider(configured ? [ChannelDiscordId] : [], jobClient, automaticIndexing);
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MemeIndexSweepJob>().ExecuteAsync(CancellationToken.None);
    }

    // automaticIndexing: true = the live hook and the weekly sweep are switched on (#369). The
    // option's own default is false; the tests of that default pass false here.
    private ServiceProvider BuildProvider(
        ulong[]? channelIds = null, IBackgroundJobClient? jobClient = null, bool automaticIndexing = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DiscordDbContext>(o => o
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention());
        services.Configure<MemeIndexOptions>(o =>
        {
            o.ChannelIds = channelIds ?? [ChannelDiscordId];
            o.AutomaticIndexing = automaticIndexing;
        });
        services.Configure<OpenRouterOptions>(o =>
        {
            o.ApiKey = "test-key";
            o.Model = ConfiguredModel;
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

    // Exactly one annotation per attachment, each under the configured writer's key.
    private static async Task AssertConfiguredWriterAnnotationsAsync(DiscordDbContext verify, params ulong[] attachmentIds)
    {
        var annotations = await verify.MemeAnnotations
            .Include(a => a.MemeIndex)
            .OrderBy(a => a.AttachmentDiscordId)
            .ToListAsync();
        Assert.Equal(attachmentIds, annotations.Select(a => a.AttachmentDiscordId));
        Assert.All(annotations, a =>
        {
            Assert.Equal(a.AttachmentDiscordId, a.MemeIndex.AttachmentDiscordId);
            Assert.Equal(MemeIndexStatus.Indexed, a.MemeIndex.Status);
            Assert.Equal(ConfiguredModel, a.ModelId);
            Assert.Equal(OpenRouterClient.PromptVersion, a.PromptVersion);
            Assert.NotNull(a.RawResponseJson);
        });
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
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}

// Reads the checkpoint row from a fresh context at Create time — what cancel or the startup
// sweep would see in the window between the enqueuer's two saves.
internal sealed class SnapshottingJobClient(Func<DiscordDbContext> newContext, ulong guildId) : IBackgroundJobClient
{
    public BackfillStatus? StatusAtCreate { get; private set; }
    public string? JobIdAtCreate { get; private set; }

    public string Create(Job job, IState state)
    {
        using var db = newContext();
        var row = db.BackfillCheckpoints.Single(c => c.GuildDiscordId == guildId && c.Type == BackfillType.MemeIndex);
        StatusAtCreate = row.Status;
        JobIdAtCreate = row.HangfireJobId;
        return "new-job";
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}

// Cancels the caller's token the moment the job is created, so every save after the enqueue
// runs under an already-cancelled token.
internal sealed class CancelOnCreateJobClient(IBackgroundJobClient inner, CancellationTokenSource cts) : IBackgroundJobClient
{
    public string Create(Job job, IState state)
    {
        var id = inner.Create(job, state);
        cts.Cancel();
        return id;
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => inner.ChangeState(jobId, state, expectedState);
}

// Captures enqueues and state changes (e.g. Delete) without a storage backend.
internal sealed class RecordingJobClient : IBackgroundJobClient
{
    public List<Job> Created { get; } = [];
    public List<(string JobId, string State)> StateChanges { get; } = [];

    public string Create(Job job, IState state)
    {
        Created.Add(job);
        return Created.Count.ToString();
    }

    public bool ChangeState(string jobId, IState state, string expectedState)
    {
        StateChanges.Add((jobId, state.Name));
        return true;
    }
}
