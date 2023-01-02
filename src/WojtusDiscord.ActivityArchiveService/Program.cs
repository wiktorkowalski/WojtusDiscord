using dotenv.net;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Sinks.Grafana.Loki;
using Serilog.Sinks.SystemConsole.Themes;
using WojtusDiscord.ActivityArchiveService;
using WojtusDiscord.ActivityArchiveService.Config;
using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Services;

var builder = WebApplication.CreateBuilder(args);

DotEnv.Fluent()
    .WithExceptions()
    .WithEnvFiles(".env")
    .WithTrimValues()
    .WithOverwriteExistingVars()
    .WithProbeForEnv(probeLevelsToSearch: 6)
    .Load();

builder.Configuration
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables();

builder.Services.Configure<DatabaseConfig>(builder.Configuration);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(theme: AnsiConsoleTheme.Code, outputTemplate: "[{Timestamp:HH:mm:ss}|{Level:u4}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.GrafanaLoki("http://localhost:3100", labels: new[] { new LokiLabel { Key = "service", Value = "ActivityArchiveService" } })
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);

builder.Services.AddOpenTelemetryTracing(builder =>
{
    builder.AddJaegerExporter(config =>
    {
        config.AgentHost = "localhost";
        config.AgentPort = 6831;
    })
    .AddSource("ActivityArchiveService")
    .SetResourceBuilder(
        ResourceBuilder.CreateDefault()
        .AddService(serviceName: "ActivityArchiveService", serviceVersion: "1.0.0"))
    .AddHttpClientInstrumentation()
    .AddAspNetCoreInstrumentation();
    
});

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<DiscordUserService>();
builder.Services.AddScoped<DiscordGuildService>();
builder.Services.AddScoped<DiscordGuildMemberService>();
builder.Services.AddScoped<DiscordChannelService>();
builder.Services.AddScoped<DiscordEmoteService>();
builder.Services.AddScoped<DiscordMessageService>();
builder.Services.AddScoped<DiscordReactionService>();
builder.Services.AddScoped<GuildInitializerService>();

builder.Services.AddHostedService<DiscordService>();

builder.Services.AddDbContext<ActivityArchiveContext>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
