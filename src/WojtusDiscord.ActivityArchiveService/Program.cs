using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using WojtusDiscord.ActivityArchiveService;
using WojtusDiscord.ActivityArchiveService.Database;
using WojtusDiscord.ActivityArchiveService.Services;

var builder = WebApplication.CreateBuilder(args);
DotNetEnv.Env.TraversePath().Load();

var logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(theme: AnsiConsoleTheme.Code, outputTemplate: "[{Timestamp:HH:mm:ss}|{Level:u4}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(logger);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<DiscordUserService>();
builder.Services.AddScoped<DiscordGuildService>();
builder.Services.AddScoped<DiscordChannelService>();
builder.Services.AddScoped<DiscordEmoteService>();
builder.Services.AddScoped<DiscordMessageService>();
builder.Services.AddScoped<DiscordReactionService>();
builder.Services.AddScoped<GuildInitializerService>();

builder.Services.AddHostedService<DiscordService>();

builder.Services.AddDbContext<ActivityArchiveContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetValue<string>("PostgresConnectionString"));
    options.UseSnakeCaseNamingConvention();
});

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
