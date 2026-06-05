using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Endpoints;
using DiscordEventService.Infrastructure;
using DiscordEventService.Jobs;
using DiscordEventService.Services;
using DiscordEventService.Services.EventHandlers;
using DiscordEventService.Services.Pipeline;
using DotNetEnv;
using DSharpPlus;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Load .env from repo root (two levels up from project directory)
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var envPath = Path.Combine(repoRoot, ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
}

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

// Configuration with validation
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services.AddOptions<DiscordOptions>()
    .Bind(builder.Configuration.GetSection(DiscordOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<HealthCheckOptions>()
    .Bind(builder.Configuration.GetSection(HealthCheckOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");

// Database with connection resiliency and snake_case naming
builder.Services.AddDbContext<DiscordDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null))
    .UseSnakeCaseNamingConvention());

// Discord Client with services configured for DI
var discordToken = builder.Configuration.GetSection(DiscordOptions.SectionName).Get<DiscordOptions>()?.Token
    ?? throw new InvalidOperationException("Discord:Token is required");

builder.Services.AddSingleton(rootSp =>
{
    var clientBuilder = DiscordClientBuilder.CreateDefault(discordToken, DiscordIntents.All);

    // Register ASP.NET Core services in DSharpPlus's DI container. Services event
    // handlers share with the root container come from AddCoreServices(); add new
    // shared services to CoreServiceRegistration.CoreServiceTypes (one line, both
    // containers + StartupValidator pick them up automatically).
    clientBuilder.ConfigureServices(services =>
    {
        services.AddDbContext<DiscordDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddSingleton<IHostEnvironment>(builder.Environment);
        services.AddSingleton<EventPipeline>();
        services.AddCoreServices();
        // IBackgroundJobClient forwards to the root container's registration.
        // Hangfire registers IBackgroundJobClient as Transient with DI-based
        // JobStorage resolution; using `new BackgroundJobClient()` directly
        // would read JobStorage.Current (a static set by AddHangfireServer's
        // HostedService at app.Run() time), which is null at validator time
        // post-Build but pre-Run. Forwarding resolves through Hangfire's
        // DI-aware factory which works at any time after Build.
        services.AddSingleton<IBackgroundJobClient>(_ => rootSp.GetRequiredService<IBackgroundJobClient>());
        services.AddMemoryCache();
    });

    // Event handlers
    clientBuilder.ConfigureEventHandlers(b => b
        // Messages & Reactions
        .AddEventHandlers<MessageEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<ReactionEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<PollEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<PinEventHandler>(ServiceLifetime.Scoped)
        // Voice & Presence
        .AddEventHandlers<VoiceEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<VoiceServerEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<PresenceEventHandler>(ServiceLifetime.Scoped)
        // Members & Bans
        .AddEventHandlers<MemberEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<BanEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<GuildMembersChunkHandler>(ServiceLifetime.Scoped)
        // Guild updates
        .AddEventHandlers<GuildEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<GuildUpdateEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<EmojiEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<StickerEventHandler>(ServiceLifetime.Scoped)
        // Channels, Roles & Threads
        .AddEventHandlers<ChannelEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<RoleEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<ThreadEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<ThreadSyncHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<StageInstanceEventHandler>(ServiceLifetime.Scoped)
        // Scheduled Events & AutoMod
        .AddEventHandlers<ScheduledEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<AutoModEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<AutoModRuleEventHandler>(ServiceLifetime.Scoped)
        // Invites & Typing
        .AddEventHandlers<InviteEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<TypingEventHandler>(ServiceLifetime.Scoped)
        // Webhooks & Integrations
        .AddEventHandlers<WebhookEventHandler>(ServiceLifetime.Scoped)
        .AddEventHandlers<IntegrationEventHandler>(ServiceLifetime.Scoped)
        // Audit Log
        .AddEventHandlers<AuditLogEventHandler>(ServiceLifetime.Scoped)
        // Socket lifecycle (downtime tracking)
        .AddEventHandlers<SocketLifecycleHandler>(ServiceLifetime.Scoped)
    );

    return clientBuilder.Build();
});

// Hosted Service
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

// Health checks
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

// Hangfire
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
        options.UseNpgsqlConnection(connectionString)));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2;
    options.Queues = ["backfill", "default"];
});

// Backfill jobs
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

// Apply migrations if configured
var dbOptions = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
if (dbOptions.AutoMigrate)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
    await db.Database.MigrateAsync();
}

// Serve the bundled dashboard SPA (Vite build output in wwwroot). Must precede
// the route-mapping below; the SPA fallback is registered LAST so it never
// swallows /api, /health, or /hangfire.
app.UseDefaultFiles();
app.UseStaticFiles();

// Dashboard read API
app.MapControllers();

app.MapHealthChecks("/health");

// Hangfire dashboard
app.MapHangfireDashboard("/hangfire");

// Weekly full backfill safety net — catches anything reconnect-backfill misses
// due to its windowed semantics. See #124 postmortem.
RecurringJob.AddOrUpdate<PeriodicFullBackfillJob>(
    "periodic-full-backfill",
    j => j.ExecuteAsync(),
    "0 3 * * 0"); // Sundays 03:00 UTC

RecurringJob.AddOrUpdate<HealthCheckJob>(
    "health-check",
    j => j.ExecuteAsync(),
    "*/5 * * * *"); // Every 5 minutes

// Backfill API
app.MapBackfillEndpoints();

// Operations API
app.MapOpsEndpoints();

// SPA fallback — LAST so it only catches client-side routes (any non-/api,
// non-/health, non-/hangfire GET) and serves index.html for deep links.
app.MapFallbackToFile("index.html");

app.Run();
