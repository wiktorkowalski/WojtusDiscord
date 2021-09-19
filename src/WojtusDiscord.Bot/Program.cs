using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WojtusDiscord.Bot;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<DiscordBot>();
    })
    .Build();

await host.RunAsync();
