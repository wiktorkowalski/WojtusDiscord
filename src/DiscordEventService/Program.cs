using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Endpoints;
using DiscordEventService.Infrastructure;
using DiscordEventService.Jobs;
using DiscordEventService.Services;
using DiscordEventService.Services.Conversation;
using DiscordEventService.Services.MemeIndexing;
using DotNetEnv;
using DSharpPlus;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Load .env from repo root (two levels up from project directory)
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var envPath = Path.Combine(repoRoot, ".env");
if (File.Exists(envPath)) Env.Load(envPath);

var builder = WebApplication.CreateBuilder(args);

// Fail fast on DI misconfigurations: ValidateOnBuild constructs every
// registered service at host build time so missing/unresolvable dependencies
// surface immediately, not when the dependent path first fires at runtime.
// ValidateScopes catches captive-dependency mistakes (singleton consuming
// scoped); kept dev-only because of per-resolution overhead.
builder.Host.UseDefaultServiceProvider(opts =>
{
    opts.ValidateOnBuild = true;
    opts.ValidateScopes = builder.Environment.IsDevelopment();
});

builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services.AddOptions<DiscordOptions>()
    .Bind(builder.Configuration.GetSection(DiscordOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<HealthCheckOptions>()
    .Bind(builder.Configuration.GetSection(HealthCheckOptions.SectionName));

// Meme indexing (#218): both intentionally boot-safe when unconfigured — the
// benchmark/indexing endpoints reject with 400 instead of failing startup.
// OPENROUTER_API_KEY (the ecosystem-conventional name) is accepted as a
// fallback for OpenRouter__ApiKey.
builder.Services.AddOptions<OpenRouterOptions>()
    .Bind(builder.Configuration.GetSection(OpenRouterOptions.SectionName))
    .PostConfigure(options =>
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            options.ApiKey = builder.Configuration["OPENROUTER_API_KEY"];
    });

builder.Services.AddOptions<MemeIndexOptions>()
    .Bind(builder.Configuration.GetSection(MemeIndexOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");

// Database with connection resiliency and snake_case naming; registered identically
// in the root container and the DSharpPlus child container.
const int dbMaxRetryCount = 3;
var dbMaxRetryDelay = TimeSpan.FromSeconds(5);

void AddDiscordDbContext(IServiceCollection services) =>
    services.AddDbContext<DiscordDbContext>(options =>
        options.UseNpgsql(
            connectionString,
            npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: dbMaxRetryCount,
                maxRetryDelay: dbMaxRetryDelay,
                errorCodesToAdd: null))
        .UseSnakeCaseNamingConvention());

AddDiscordDbContext(builder.Services);

// Discord Client with services configured for DI: child container, event-handler
// roster and slash commands live in DiscordClientRegistration.
builder.Services.AddDiscordClient(builder.Configuration, builder.Environment, AddDiscordDbContext);

// Order is load-bearing: DiscordHostedService.StartAsync runs InferStartupGapAsync
// (against the stale last_heartbeat_utc) before HeartbeatBackgroundService can write
// a fresh tick that would mask the gap. Do not reorder.
builder.Services.AddHostedService<DiscordHostedService>();
builder.Services.AddHostedService<HeartbeatBackgroundService>();

// Memory cache for throttling
builder.Services.AddMemoryCache();

// Shared services (registered in both the root and DSharpPlus child containers)
builder.Services.AddCoreServices();

// Root-only services (backfill/ops paths; event handlers don't resolve these)
builder.Services.AddScoped<OrphanReplayService>();
builder.Services.AddScoped<ThreadChannelBackfillService>();
builder.Services.AddScoped<MemberRoleSnapshotBackfillService>();
builder.Services.AddScoped<MessageMentionsBackfillService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<DiscordDbContext>();

// Dashboard read API (MVC controllers under Controllers/). Controllers are
// auto-discovered by MapControllers(); no per-controller registration needed.
// Snowflakes serialize as strings (JS number precision) for the whole API.
builder.Services.AddControllers()
    .AddJsonOptions(o => DashboardJson.Configure(o.JsonSerializerOptions));

// Startup-cached whitelist of explorable tables/columns for the generic explorer,
// built from the EF model. Constructing a DbContext to read its Model needs no DB
// connection, so this is safe at DI-validation time.
builder.Services.AddSingleton(sp =>
{
    using var scope = sp.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
    return SchemaCatalog.Build(db.Model);
});

builder.Services.AddHttpClient();
builder.Services.AddScoped<HealthCheckJob>();

// Meme indexing (#219): OpenRouter vision calls + Discord CDN image downloads.
var openRouterTimeout = TimeSpan.FromMinutes(2);
var memeDownloadTimeout = TimeSpan.FromMinutes(1);
var urlRefreshTimeout = TimeSpan.FromSeconds(30);

builder.Services.AddHttpClient(OpenRouterClient.HttpClientName)
    .ConfigureHttpClient((sp, client) =>
    {
        var openRouter = sp.GetRequiredService<IOptions<OpenRouterOptions>>().Value;
        client.BaseAddress = new Uri(openRouter.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = openRouterTimeout;
    });

builder.Services.AddHttpClient(MemeBenchmarkJob.DownloadHttpClientName)
    .ConfigureHttpClient(client => client.Timeout = memeDownloadTimeout);

builder.Services.AddHttpClient(AttachmentUrlRefreshService.HttpClientName)
    .ConfigureHttpClient(client =>
    {
        client.BaseAddress = new Uri("https://discord.com/api/v10/");
        client.Timeout = urlRefreshTimeout;
    });

builder.Services.AddScoped<OpenRouterClient>();
builder.Services.AddScoped<MemeSampleService>();
builder.Services.AddScoped<AttachmentUrlRefreshService>();
builder.Services.AddScoped<MemeAttachmentIndexer>();
builder.Services.AddScoped<MemeBenchmarkJob>();
builder.Services.AddScoped<MemeIndexingJob>();
builder.Services.AddScoped<MemeIndexSweepJob>();

// Conversational assistant (#238, ADR-0006): the MEAI IChatClient over OpenRouter +
// Langfuse OTel export. Registers the singleton chat client in the root container; the
// DSharpPlus child container forwards to it (ConversationRegistration). ConversationService
// is a shared scoped service (CoreServiceTypes); the handler lives in DiscordClientRegistration.
builder.Services.AddConversationFeature(builder.Configuration);

// Hangfire. With a fixed InvisibilityTimeout a job outliving it gets presumed
// dead, re-queued, and the original execution cancelled — observed live on the
// 28-min meme benchmark (#219), restarting it from scratch in a pay-per-run
// loop. Sliding mode instead heartbeats the fetched timestamp while the worker
// is alive, so arbitrarily long jobs (full-corpus indexing, #221) are safe and
// the timeout only governs how fast a genuinely crashed worker's job is
// re-picked. Sliding requires the storage background processes — incompatible
// with BackgroundJobServerOptions.IsLightweightServer.
var hangfireInvisibilityTimeout = TimeSpan.FromMinutes(15);
const int hangfireWorkerCount = 2;

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        options => options.UseNpgsqlConnection(connectionString),
        new Hangfire.PostgreSql.PostgreSqlStorageOptions
        {
            UseSlidingInvisibilityTimeout = true,
            InvisibilityTimeout = hangfireInvisibilityTimeout
        }));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = hangfireWorkerCount;
    options.Queues = ["backfill", "default"];
});

builder.Services.AddScoped<BackfillJobExecutor>();
builder.Services.AddScoped<RolesBackfillJob>();
builder.Services.AddScoped<EmojisBackfillJob>();
builder.Services.AddScoped<StickersBackfillJob>();
builder.Services.AddScoped<ChannelsBackfillJob>();
builder.Services.AddScoped<MembersBackfillJob>();
builder.Services.AddScoped<MessagesBackfillJob>();
builder.Services.AddScoped<ReactionsBackfillJob>();
builder.Services.AddScoped<PeriodicFullBackfillJob>();

var app = builder.Build();

// Validate the DSharpPlus child DI container by resolving each service that
// event handlers depend on. The root container's ValidateOnBuild can't reach
// the child container's IServiceProvider, so we do it explicitly here. Throws
// at startup (before app.Run()) if anything is missing or misregistered.
{
    var discordClient = app.Services.GetRequiredService<DiscordClient>();
    StartupValidator.ValidateChildContainer(
        discordClient.ServiceProvider,
        app.Services.GetRequiredService<ILogger<Program>>());
}

// VALIDATE_AND_EXIT=1 short-circuits startup right after DI validation.
// Useful as a CI/local smoke test to confirm registrations are correct
// without booting the bot.
if (Environment.GetEnvironmentVariable("VALIDATE_AND_EXIT") == "1")
{
    app.Services.GetRequiredService<ILogger<Program>>()
        .LogInformation("VALIDATE_AND_EXIT=1 set, exiting after DI validation.");
    return;
}

var dbOptions = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
if (dbOptions.AutoMigrate)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
    await db.Database.MigrateAsync();
}

// query_database (#238 §4): in Development, provision the non-superuser SELECT-only role the tool drops
// into (idempotent, run as the EF owner) so it works out of the box. In production this is a documented
// one-time MANUAL step — CREATE ROLE is a privileged write against the read-only-by-convention prod DB —
// and until it runs the tool fails closed (SET LOCAL ROLE errors) rather than querying as the superuser.
if (app.Environment.IsDevelopment())
{
    var queryRole = app.Services.GetRequiredService<IOptions<ConversationOptions>>().Value.QueryRoleName;
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    try
    {
        await QueryRoleProvisioner.ProvisionAsync(connectionString, queryRole, CancellationToken.None);
        logger.LogInformation("Provisioned the read-only query role {Role} (Development)", queryRole);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to provision the read-only query role; query_database will fail closed");
    }
}

// Serve the bundled dashboard SPA (Vite build output in wwwroot). Must precede
// the route-mapping below; the SPA fallback is registered LAST so it never
// swallows /api, /health, or /hangfire.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.MapHealthChecks("/health");

app.MapHangfireDashboard("/hangfire");

// Weekly full backfill safety net — catches anything reconnect-backfill misses
// due to its windowed semantics. See #124 postmortem.
RecurringJob.AddOrUpdate<PeriodicFullBackfillJob>(
    "periodic-full-backfill",
    j => j.ExecuteAsync(),
    "0 3 * * 0"); // Sundays 03:00 UTC

RecurringJob.AddOrUpdate<HealthCheckJob>(
    "health-check",
    // Hangfire swaps CancellationToken.None for the real shutdown token at execution.
    j => j.ExecuteAsync(CancellationToken.None),
    "*/5 * * * *"); // Every 5 minutes

// Weekly meme-index healing sweep (#222) — indexes attachments the live
// MessageCreated path missed (downtime, enqueue failures) and retries Failed
// rows under the attempt cap. After the 03:00 full backfill so messages it
// recovers are already in the DB. No-op while meme indexing is unconfigured.
RecurringJob.AddOrUpdate<MemeIndexSweepJob>(
    "meme-index-sweep",
    j => j.ExecuteAsync(CancellationToken.None),
    "0 5 * * 0"); // Sundays 05:00 UTC

app.MapBackfillEndpoints();

app.MapOpsEndpoints();

app.MapMemeBenchmarkEndpoints();
app.MapMemeIndexEndpoints();

// SPA fallback — LAST so it only catches client-side routes (any non-/api,
// non-/health, non-/hangfire GET) and serves index.html for deep links.
app.MapFallbackToFile("index.html");

app.Run();
