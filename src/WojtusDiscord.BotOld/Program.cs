using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WojtusDiscord.Bot;
using WojtusDiscord.Bot.Services;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<DiscordBot>();
        services.AddSingleton<IPubSubService, RedisPubSubService>();
    })
    .Build();

await host.RunAsync();
