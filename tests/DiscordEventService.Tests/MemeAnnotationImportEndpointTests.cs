using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Endpoints;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiscordEventService.Tests;

// #369 at the HTTP level: the secret gate, the body checks and the JSON that leaves the endpoint.
// Program.cs boots Discord, so each test starts a small Kestrel host that maps the real endpoint
// over the real services, and calls it with a real HttpClient.
public sealed class MemeAnnotationImportEndpointTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 1UL;
    private const ulong ChannelDiscordId = 2UL;
    private const string Secret = "correct horse battery staple";
    private const string ImportPath = "/api/ops/meme-annotations/import";
    private const string ImportModel = "anthropic/claude-opus-5.5";
    // One item that is valid JSON and is rejected by the service: enough to get past the gate
    // and the body checks without any seeded attachment.
    private const string OneItemBody = "[{}]";

    private DiscordDbContext _db = null!;
    private GuildEntity _guild = null!;
    private ChannelEntity _channel = null!;
    private UserEntity _author = null!;
    private readonly FakeMemeHttpHandler _payloads = new();

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
        _author = new UserEntity { DiscordId = 3UL, Username = "u" };
        _db.Channels.Add(_channel);
        _db.Users.Add(_author);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // The second acceptance criterion of #369.
    [Fact]
    public async Task Import_NoSecretHeader_Returns401()
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync(OneItemBody, secret: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("")]
    [InlineData("correct horse battery staple!")]  // one character more
    [InlineData("correct horse battery stapl")]    // one character less
    [InlineData("Correct horse battery staple")]   // another case
    public async Task Import_WrongSecret_Returns401(string sent)
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync(OneItemBody, sent);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Import_RightSecret_Returns200()
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync(OneItemBody, Secret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // An unset secret must not mean an open endpoint, and it must not mean "the empty secret":
    // a client that sends an empty header would match "" otherwise.
    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData(null, "anything")]
    [InlineData("", null)]
    [InlineData("", "")]
    [InlineData("", "anything")]
    // Whitespace only is not a secret either: the configured value is trimmed.
    [InlineData("   ", null)]
    [InlineData("   ", "")]
    [InlineData("   ", "   ")]
    [InlineData("   ", "anything")]
    public async Task Import_SecretNotConfigured_Returns401WhateverIsSent(string? configured, string? sent)
    {
        await using var host = await StartAsync(o => o.ImportSecret = configured);

        var response = await host.PostAsync(OneItemBody, sent);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // HTTP drops the whitespace around a header value, so a configured secret that kept its own
    // (a trailing newline from a secret file) could never match. The configured value is trimmed.
    [Theory]
    [InlineData(" s3cret ")]
    [InlineData("s3cret\n")]
    [InlineData("\ts3cret")]
    public async Task Import_ConfiguredSecretWithOuterWhitespace_MatchesTheTrimmedHeader(string configured)
    {
        await using var host = await StartAsync(o => o.ImportSecret = configured);

        var right = await host.PostAsync(OneItemBody, "s3cret");
        var wrong = await host.PostAsync(OneItemBody, "s3cre");

        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    // A running indexing job holds its rows in memory and would write its own status over the
    // import's. Queued counts as running: the job starts at any moment.
    [Theory]
    [InlineData(BackfillStatus.InProgress)]
    [InlineData(BackfillStatus.Pending)]
    public async Task Import_MemeIndexingActive_Returns409AndWritesNothing(BackfillStatus status)
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await SeedCheckpointAsync(BackfillType.MemeIndex, status, stale: false);
        await using var host = await StartAsync();

        var response = await host.PostAsync(new JsonArray(Item(11UL, Metadata(1))).ToJsonString(), Secret);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("error").GetString()));
        await using var verify = NewContext();
        Assert.Equal(0, await verify.MemeAnnotations.CountAsync());
        Assert.Equal(0, await verify.MemeIndex.CountAsync());
    }

    // A checkpoint with no heartbeat for longer than StaleInProgressAfter belongs to a dead
    // process (#282): it must not block the import for ever.
    [Theory]
    [InlineData(BackfillStatus.InProgress)]
    [InlineData(BackfillStatus.Pending)]
    public async Task Import_StaleMemeIndexingCheckpoint_Returns200AndImports(BackfillStatus status)
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await SeedCheckpointAsync(BackfillType.MemeIndex, status, stale: true);
        await using var host = await StartAsync();

        var response = await host.PostAsync(new JsonArray(Item(11UL, Metadata(1))).ToJsonString(), Secret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verify = NewContext();
        Assert.Equal(11UL, (await verify.MemeAnnotations.SingleAsync()).AttachmentDiscordId);
    }

    // Only a live meme indexing run blocks: a finished one does not, and neither does another
    // kind of backfill that is running.
    [Theory]
    [InlineData(BackfillType.MemeIndex, BackfillStatus.Completed)]
    [InlineData(BackfillType.MemeIndex, BackfillStatus.Failed)]
    [InlineData(BackfillType.Roles, BackfillStatus.InProgress)]
    public async Task Import_CheckpointThatIsNotALiveMemeIndexingRun_Returns200(BackfillType type, BackfillStatus status)
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await SeedCheckpointAsync(type, status, stale: false);
        await using var host = await StartAsync();

        var response = await host.PostAsync(new JsonArray(Item(11UL, Metadata(1))).ToJsonString(), Secret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verify = NewContext();
        Assert.Equal(1, await verify.MemeAnnotations.CountAsync());
    }

    // The gate is first here too: without the secret nobody learns that a job is running.
    [Fact]
    public async Task Import_NoSecretWhileMemeIndexingIsActive_Returns401()
    {
        await SeedCheckpointAsync(BackfillType.MemeIndex, BackfillStatus.InProgress, stale: false);
        await using var host = await StartAsync();

        var response = await host.PostAsync(OneItemBody, secret: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The gate runs before the handler: a caller without the secret learns nothing about the
    // body checks or the configuration. Each of these is a 400 with the secret.
    [Theory]
    [InlineData("{ this is not json", "application/json", true)]
    [InlineData("{}", "application/json", true)]
    [InlineData("[]", "application/json", true)]
    [InlineData(OneItemBody, "text/plain", true)]
    [InlineData(OneItemBody, "application/json", false)]
    public async Task Import_NoSecretAndARequestThatWouldBeA400_Returns401(string body, string contentType, bool channelsConfigured)
    {
        await using var host = await StartAsync(o => o.ChannelIds = channelsConfigured ? [ChannelDiscordId] : []);

        var response = await host.PostAsync(body, secret: null, contentType);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var verify = NewContext();
        Assert.Equal(0, await verify.MemeAnnotations.CountAsync());
    }

    // Without ChannelIds no attachment is a meme: every item would be rejected one by one.
    // One clear answer instead.
    [Fact]
    public async Task Import_NoMemeChannelsConfigured_Returns400()
    {
        await using var host = await StartAsync(o => o.ChannelIds = []);

        var response = await host.PostAsync(OneItemBody, Secret);

        await AssertBadRequestAsync(response, "ChannelIds");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"items\":[]}")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("null")]
    public async Task Import_BodyThatIsNotAnArray_Returns400(string body)
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync(body, Secret);

        await AssertBadRequestAsync(response, "array");
    }

    [Fact]
    public async Task Import_EmptyArray_Returns400()
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync("[]", Secret);

        await AssertBadRequestAsync(response, "empty");
    }

    [Fact]
    public async Task Import_OneItemMoreThanTheBatchLimit_Returns400AndWritesNothing()
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        // The first item is valid: a 400 for the size must not import a part of the batch.
        var items = new JsonArray(Item(11UL, Metadata(1)));
        while (items.Count < MemeAnnotationImportEndpoints.MaxBatchSize + 1)
            items.Add(new JsonObject());
        await using var host = await StartAsync();

        var response = await host.PostAsync(items.ToJsonString(), Secret);

        await AssertBadRequestAsync(response, MemeAnnotationImportEndpoints.MaxBatchSize.ToString());
        await using var verify = NewContext();
        Assert.Equal(0, await verify.MemeAnnotations.CountAsync());
        Assert.Equal(0, await verify.MemeIndex.CountAsync());
    }

    // The limit itself is a legal batch.
    [Fact]
    public async Task Import_ExactlyTheBatchLimit_Returns200WithOneResultPerItem()
    {
        var items = new JsonArray();
        while (items.Count < MemeAnnotationImportEndpoints.MaxBatchSize)
            items.Add(new JsonObject());
        await using var host = await StartAsync();

        var response = await host.PostAsync(items.ToJsonString(), Secret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(MemeAnnotationImportEndpoints.MaxBatchSize, json.RootElement.GetProperty("rejected").GetInt32());
        Assert.Equal(MemeAnnotationImportEndpoints.MaxBatchSize, json.RootElement.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("[{\"attachment_discord_id\": }]")]
    [InlineData("[{}")]
    [InlineData("")]
    public async Task Import_InvalidJson_Returns400(string body)
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync(body, Secret);

        await AssertBadRequestAsync(response, "JSON");
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData(null)]
    public async Task Import_ContentTypeThatIsNotJson_Returns400(string? contentType)
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync(OneItemBody, Secret, contentType);

        await AssertBadRequestAsync(response, "Content-Type");
    }

    // The runbook's jq lines read these names (camelCase, the minimal-API default): a rename here
    // breaks `jq '{imported, overwritten, skipped, rejected}'` without any compile error.
    [Fact]
    public async Task Import_MixedBatch_ReturnsTheCountsAndOneResultPerItemInCamelCase()
    {
        AddMessage(1001UL, Attachment(11UL, "new.png"), Attachment(12UL, "same.png"), Attachment(13UL, "changed.png"));
        await _db.SaveChangesAsync();
        await using var host = await StartAsync();
        var seeded = await host.PostAsync(
            new JsonArray(Item(12UL, Metadata(2)), Item(13UL, Metadata(3))).ToJsonString(), Secret);
        Assert.Equal(HttpStatusCode.OK, seeded.StatusCode);
        var changed = Metadata(3);
        changed["description_pl"] = "Poprawiony opis";

        var response = await host.PostAsync(new JsonArray(
            Item(11UL, Metadata(1)),   // new key            -> imported
            Item(12UL, Metadata(2)),   // same metadata      -> skipped
            Item(13UL, changed),       // other metadata     -> imported, overwritten
            Item(404UL, Metadata(4)),  // unknown attachment -> rejected
            JsonValue.Create(42)       // not an object      -> rejected, no attachment id
        ).ToJsonString(), Secret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(["imported", "items", "overwritten", "rejected", "skipped"], root.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(2, root.GetProperty("imported").GetInt32());
        Assert.Equal(1, root.GetProperty("overwritten").GetInt32());
        Assert.Equal(1, root.GetProperty("skipped").GetInt32());
        Assert.Equal(2, root.GetProperty("rejected").GetInt32());

        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(5, items.Count);
        Assert.All(items, item => Assert.Equal(
            ["attachmentDiscordId", "index", "outcome", "overwritten", "reason"],
            item.EnumerateObject().Select(p => p.Name).Order()));
        Assert.Equal([0, 1, 2, 3, 4], items.Select(i => i.GetProperty("index").GetInt32()));
        Assert.Equal(["imported", "skipped", "imported", "rejected", "rejected"],
            items.Select(i => i.GetProperty("outcome").GetString()));
        Assert.Equal([false, false, true, false, false], items.Select(i => i.GetProperty("overwritten").GetBoolean()));
        // A snowflake leaves as a string; null when the item did not parse.
        Assert.Equal(["11", "12", "13", "404", null], items.Select(i => i.GetProperty("attachmentDiscordId").GetString()));
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("reason").ValueKind);
        Assert.All(items.Skip(1).Where((_, i) => i != 1),
            item => Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("reason").GetString())));

        await using var verify = NewContext();
        Assert.Equal([11UL, 12UL, 13UL],
            await verify.MemeAnnotations.OrderBy(a => a.AttachmentDiscordId).Select(a => a.AttachmentDiscordId).ToListAsync());
        Assert.Equal("Poprawiony opis", (await verify.MemeAnnotations.SingleAsync(a => a.AttachmentDiscordId == 13UL)).DescriptionPl);
    }

    // The route and the verb are what the runbook's curl line uses.
    [Fact]
    public async Task Import_GetOnTheImportRoute_IsNotAllowed()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync(ImportPath);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // Saves the pending messages too.
    private async Task SeedCheckpointAsync(BackfillType type, BackfillStatus status, bool stale)
    {
        _db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildDiscordId,
            Type = type,
            Status = status,
            StartedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        if (!stale)
            return;

        // ITimestamped bumps LastUpdatedUtc on every SaveChanges; ExecuteUpdate bypasses it so the
        // heartbeat can be aged past the staleness cutoff.
        await _db.BackfillCheckpoints.ExecuteUpdateAsync(s => s.SetProperty(
            c => c.LastUpdatedUtc,
            DateTime.UtcNow - BackfillCheckpointEntity.StaleInProgressAfter - TimeSpan.FromMinutes(1)));
    }

    private static async Task AssertBadRequestAsync(HttpResponseMessage response, string errorContains)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(errorContains, json.RootElement.GetProperty("error").GetString());
    }

    // Defaults: the meme channel and the secret are configured. `configure` changes one of them.
    private async Task<ImportHost> StartAsync(Action<MemeIndexOptions>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        // No ConfigureHttpJsonOptions, like Program.cs: DashboardJson there is for MVC controllers
        // only, so the endpoint's JSON is the minimal-API default.
        builder.Services.AddDbContext<DiscordDbContext>(o => o
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention());
        builder.Services.Configure<MemeIndexOptions>(o =>
        {
            o.ChannelIds = [ChannelDiscordId];
            o.ImportSecret = Secret;
            configure?.Invoke(o);
        });
        builder.Services.Configure<OpenRouterOptions>(o =>
        {
            o.ApiKey = "test-key";
            o.Model = "test/model";
            o.RequestDelayMs = 0;
        });
        // The indexer's constructor needs both; an import must never reach either.
        builder.Services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(_payloads));
        builder.Services.AddScoped<OpenRouterClient>();
        builder.Services.AddScoped<MemeSampleService>();
        builder.Services.AddScoped<MemeAttachmentIndexer>();
        builder.Services.AddScoped<MemeAnnotationImportService>();

        var app = builder.Build();
        app.MapMemeAnnotationImportEndpoints();
        await app.StartAsync();

        // app.Urls still holds port 0; the server knows the port it was given.
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new ImportHost(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    private sealed class ImportHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;

        // secret: null = the header is not sent. contentType: null = no Content-Type header.
        public Task<HttpResponseMessage> PostAsync(string body, string? secret, string? contentType = "application/json")
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            if (contentType is not null)
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            var request = new HttpRequestMessage(HttpMethod.Post, ImportPath) { Content = content };
            if (secret is not null)
                request.Headers.TryAddWithoutValidation(MemeAnnotationImportEndpoints.SecretHeaderName, secret);
            return client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static JsonObject Item(ulong attachmentId, JsonObject metadata) => new JsonObject
    {
        ["attachment_discord_id"] = attachmentId.ToString(),
        ["model_id"] = ImportModel,
        ["prompt_version"] = OpenRouterClient.PromptVersion,
        ["metadata"] = metadata,
    };

    // The schema v2 output FakeMemeHttpHandler would send for this image: the same contract.
    private JsonObject Metadata(byte seed) => JsonNode.Parse(_payloads.MetadataJsonFor(Png(seed)))!.AsObject();

    private void AddMessage(ulong discordId, params string[] attachments)
    {
        _db.Messages.Add(new MessageEntity
        {
            DiscordId = discordId,
            ChannelId = _channel.Id,
            GuildId = _guild.Id,
            AuthorId = _author.Id,
            HasAttachments = true,
            AttachmentsJson = $"[{string.Join(",", attachments)}]",
            CreatedAtUtc = DateTime.UtcNow
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
