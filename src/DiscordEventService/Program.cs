using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Endpoints;
using DiscordEventService.Jobs;
using DiscordEventService.Services;
using DiscordEventService.Services.EventHandlers;
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

// Configuration with validation
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services.AddOptions<DiscordOptions>()
    .Bind(builder.Configuration.GetSection(DiscordOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

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

builder.Services.AddSingleton(sp =>
{
    var clientBuilder = DiscordClientBuilder.CreateDefault(discordToken, DiscordIntents.All);

    // Register ASP.NET Core services in DSharpPlus's DI container
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

        services.AddScoped<UserService>();
        services.AddScoped<RawEventLogService>();
        services.AddScoped<FailedEventService>();
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
    );

    return clientBuilder.Build();
});

// Hosted Service
builder.Services.AddHostedService<DiscordHostedService>();

// Memory cache for throttling
builder.Services.AddMemoryCache();

// Shared services
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<RawEventLogService>();
builder.Services.AddScoped<FailedEventService>();

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DiscordDbContext>();

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
builder.Services.AddScoped<RolesBackfillJob>();
builder.Services.AddScoped<EmojisBackfillJob>();
builder.Services.AddScoped<StickersBackfillJob>();
builder.Services.AddScoped<ChannelsBackfillJob>();
builder.Services.AddScoped<MembersBackfillJob>();
builder.Services.AddScoped<MessagesBackfillJob>();
builder.Services.AddScoped<ReactionsBackfillJob>();
builder.Services.AddScoped<GuildBackfillOrchestrator>();

var app = builder.Build();

// Apply migrations if configured
var dbOptions = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
if (dbOptions.AutoMigrate)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
    await db.Database.MigrateAsync();
}

app.MapHealthChecks("/health");

// Hangfire dashboard
app.MapHangfireDashboard("/hangfire");

// Backfill API
app.MapBackfillEndpoints();

app.Run();
