using DiscordEventService.Data;
using DiscordEventService.Endpoints;
using DiscordEventService.Jobs;
using DiscordEventService.Services;
using DiscordEventService.Services.EventHandlers;
using DSharpPlus;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database with connection resiliency and snake_case naming
builder.Services.AddDbContext<DiscordDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Discord"),
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null))
    .UseSnakeCaseNamingConvention());

// Discord Client with safe disposal wrapper
builder.Services.AddSingleton<DiscordClientWrapper>(sp =>
{
    var token = builder.Configuration["Discord:Token"]
        ?? throw new InvalidOperationException("Discord:Token is required");

    var clientBuilder = DiscordClientBuilder.CreateDefault(token, DiscordIntents.All);

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

    var client = clientBuilder.Build();
    return new DiscordClientWrapper(client, sp.GetRequiredService<ILogger<DiscordClientWrapper>>());
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<DiscordClientWrapper>().Client);

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
var connectionString = builder.Configuration.GetConnectionString("Discord")
    ?? throw new InvalidOperationException("Discord connection string is required");

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
if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
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
