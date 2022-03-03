using dotenv.net;
using Serilog;
using WojtusDiscord.BotCommands;

DotEnv.Fluent()
    .WithoutExceptions()
    .WithTrimValues()
    .WithOverwriteExistingVars()
    .WithEnvFiles(".env", ".env.development")
    .Load();

var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(config)
    .MinimumLevel.Debug()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<DiscordHostedService>();
    })
    .UseSerilog()
    .Build();

try
{
    await host.RunAsync();
}
catch (Exception e)
{
    Log.Fatal(e, "Fatal error");
}
finally
{
    Log.CloseAndFlush();
}
