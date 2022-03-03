using Serilog;
using WojtusDiscord.Bot;
using Amazon.Extensions.Configuration.SystemsManager;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<DiscordHostedService>();


var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddSystemsManager("/wojtus/prod/discordtoken", TimeSpan.FromSeconds(30))
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(config)
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();

app.Map("/", () => new { Result = "WojtusDiscord.Bot is running" });

app.Run();
