using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Jobs;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace DiscordEventService.Tests;

// The from-file path needs no database: the links file is the sample.
public sealed class MemeBenchmarkJobTests : IDisposable
{
    private const ulong ChannelDiscordId = 2UL;

    private readonly string _contentRoot = Directory.CreateTempSubdirectory("meme-benchmark-tests-").FullName;
    private readonly FakeMemeHttpHandler _http = new FakeMemeHttpHandler();

    public void Dispose() => Directory.Delete(_contentRoot, recursive: true);

    [Fact]
    public async Task RunFromFileAsync_ConfiguredSlots_RunsExactlyThoseSlotsWithTheirEffort()
    {
        var linksFile = WriteLinksFile("links.json", Link(11UL, year: 2024), Link(12UL, year: 2024));
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));

        await RunFromFileAsync(linksFile, "vendor/a|effort=low", "vendor/b");

        using var report = ReadReport();
        Assert.Equal(["vendor/a|effort=low", "vendor/b"], SlotsOf(report));
        Assert.All(report.RootElement.GetProperty("Items").EnumerateArray(), item =>
            Assert.Equal(["vendor/a|effort=low", "vendor/b"],
                item.GetProperty("Cells").EnumerateArray().Select(c => c.GetProperty("Slot").GetString())));

        Assert.Equal(4, _http.ModelRequests.Count);
        Assert.Equal(2, _http.ModelRequests.Count(r => r == ("vendor/a", "low")));
        Assert.Equal(2, _http.ModelRequests.Count(r => r == ("vendor/b", null)));
    }

    [Fact]
    public async Task RunFromFileAsync_SlotsOfOneImage_AreCalledConcurrently()
    {
        var linksFile = WriteLinksFile("links.json", Link(11UL, year: 2024));
        _http.SetImage(11UL, Png(1));

        // Every model call waits until all three are in flight: sequential cells time out here.
        var inFlight = 0;
        var allInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _http.DuringModelCall = async () =>
        {
            if (Interlocked.Increment(ref inFlight) == 3)
                allInFlight.SetResult();
            await allInFlight.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };

        await RunFromFileAsync(linksFile, "vendor/a", "vendor/b", "vendor/c");

        using var report = ReadReport();
        var cells = report.RootElement.GetProperty("Items")[0].GetProperty("Cells");
        Assert.Equal(["vendor/a", "vendor/b", "vendor/c"], cells.EnumerateArray().Select(c => c.GetProperty("Slot").GetString()));
    }

    // The documented fixed-ids trick (#366): a links file built from a report's Items[*].Sample
    // replays that run's sample. Years are out of order on purpose — sampling would regroup them.
    [Fact]
    public async Task RunFromFileAsync_LinksFileBuiltFromAReport_HitsTheSameAttachmentsInTheSameOrder()
    {
        ulong[] attachmentIds = [21UL, 22UL, 23UL, 24UL, 25UL, 26UL];
        int[] years = [2024, 2019, 2024, 2021, 2019, 2022];
        var linksFile = WriteLinksFile("links.json", [.. attachmentIds.Zip(years, (id, year) => Link(id, year))]);
        foreach (var id in attachmentIds)
            _http.SetImage(id, Png((byte)id));

        await RunFromFileAsync(linksFile, "vendor/a");
        string fixedIdsFile;
        using (var firstReport = ReadReport())
        {
            Assert.Equal(attachmentIds, AttachmentIdsOf(firstReport));
            var samples = firstReport.RootElement.GetProperty("Items").EnumerateArray().Select(i => i.GetProperty("Sample"));
            fixedIdsFile = WriteLinksFile("fixed-ids.json", JsonSerializer.Serialize(samples));
        }

        await RunFromFileAsync(fixedIdsFile, "vendor/a");

        using var secondReport = ReadReport();
        Assert.Equal(attachmentIds, AttachmentIdsOf(secondReport));
    }

    [Fact]
    public async Task RunFromFileAsync_MoreImagesThanSampleSize_StillSamplesAcrossYears()
    {
        var linksFile = WriteLinksFile("links.json",
            Link(31UL, year: 2019), Link(32UL, year: 2019), Link(33UL, year: 2024), Link(34UL, year: 2024));
        foreach (var id in new[] { 31UL, 32UL, 33UL, 34UL })
            _http.SetImage(id, Png((byte)id));

        await RunFromFileAsync(linksFile, sampleSize: 2, "vendor/a");

        using var report = ReadReport();
        var sampledYears = report.RootElement.GetProperty("Items").EnumerateArray()
            .Select(i => i.GetProperty("Sample").GetProperty("CreatedAtUtc").GetDateTime().Year);
        Assert.Equal([2019, 2024], sampledYears.Order());
    }

    [Theory]
    [InlineData]
    [InlineData("vendor/a", "vendor/b|effort=nope")]
    public async Task RunFromFileAsync_NoOrInvalidSlots_MakesNoModelCallsAndWritesNoReport(params string[] slots)
    {
        var linksFile = WriteLinksFile("links.json", Link(11UL, year: 2024));
        _http.SetImage(11UL, Png(1));

        await RunFromFileAsync(linksFile, slots);

        Assert.Empty(_http.ModelRequests);
        Assert.False(Directory.Exists(MemeBenchmarkJob.ReportDirectory(NewEnvironment())));
    }

    private Task RunFromFileAsync(string linksFile, params string[] slots) =>
        RunFromFileAsync(linksFile, sampleSize: 100, slots);

    // Slots go in the way prod reads them — OpenRouter__BenchmarkModels__N through the binder —
    // so a run pins that it uses exactly the configured slots.
    private async Task RunFromFileAsync(string linksFile, int sampleSize, params string[] slots)
    {
        var settings = new Dictionary<string, string?>
        {
            ["OpenRouter:ApiKey"] = "test-key",
            ["OpenRouter:RequestDelayMs"] = "0",
        };
        foreach (var (index, slot) in slots.Index())
            settings[$"OpenRouter:BenchmarkModels:{index}"] = slot;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // Never opened: only the DB-sampling path (RunAsync) queries it.
        services.AddDbContext<DiscordDbContext>(o => o.UseNpgsql("Host=unused"));
        services.AddOptions<OpenRouterOptions>().Bind(configuration.GetSection(OpenRouterOptions.SectionName));
        services.Configure<MemeIndexOptions>(o => o.ChannelIds = [ChannelDiscordId]);
        services.Configure<DiscordOptions>(o => o.Token = new string('x', 60));
        services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(_http));
        services.AddSingleton<IWebHostEnvironment>(NewEnvironment());
        services.AddScoped<MemeSampleService>();
        services.AddScoped<AttachmentUrlRefreshService>();
        services.AddScoped<OpenRouterClient>();
        services.AddScoped<MemeBenchmarkJob>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<MemeBenchmarkJob>();
        await job.RunFromFileAsync(linksFile, sampleSize, CancellationToken.None);
    }

    private TestWebHostEnvironment NewEnvironment() => new TestWebHostEnvironment { ContentRootPath = _contentRoot };

    private string WriteLinksFile(string fileName, params MemeSampleItem[] items) =>
        WriteLinksFile(fileName, JsonSerializer.Serialize(items));

    private string WriteLinksFile(string fileName, string json)
    {
        var path = Path.Combine(_contentRoot, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    // Report names are stamped to the second, so two runs of one test can share a file: read
    // the report right after its run.
    private JsonDocument ReadReport()
    {
        var path = Directory.EnumerateFiles(MemeBenchmarkJob.ReportDirectory(NewEnvironment()), "benchmark-*.json")
            .OrderByDescending(Path.GetFileName)
            .First();
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static IEnumerable<string?> SlotsOf(JsonDocument report) =>
        report.RootElement.GetProperty("Slots").EnumerateArray().Select(s => s.GetString());

    private static IEnumerable<ulong> AttachmentIdsOf(JsonDocument report) =>
        report.RootElement.GetProperty("Items").EnumerateArray()
            .Select(i => i.GetProperty("Sample").GetProperty("AttachmentDiscordId").GetUInt64());

    private static MemeSampleItem Link(ulong attachmentId, int year) =>
        new MemeSampleItem(1UL, ChannelDiscordId, 1000UL + attachmentId, attachmentId, $"meme-{attachmentId}.png",
            new DateTime(year, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            $"https://cdn.test/attachments/{ChannelDiscordId}/{attachmentId}/meme-{attachmentId}.png?ex=expired");

    // Distinct valid-PNG-magic payloads (≥12 bytes for the sniffer).
    private static byte[] Png(byte seed) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, seed, seed, seed];

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "DiscordEventService.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
