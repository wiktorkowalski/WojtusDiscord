using DiscordEventService.Data;
using DiscordEventService.Services;
using DiscordEventService.Services.EventHandlers;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database with connection resiliency
builder.Services.AddDbContext<DiscordDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Discord"),
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null)));

// Discord Client
builder.Services.AddSingleton(sp =>
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

    return clientBuilder.Build();
});

// Hosted Service
builder.Services.AddHostedService<DiscordHostedService>();

// Memory cache for throttling
builder.Services.AddMemoryCache();

// Shared services
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<RawEventLogService>();

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DiscordDbContext>();

var app = builder.Build();

// Apply migrations in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
    await db.Database.MigrateAsync();
}

app.MapHealthChecks("/health");

app.Run();
