using NetCord.Services.ApplicationCommands;

namespace EventListenerService.Modules;

public class HealthModule : ApplicationCommandModule<ApplicationCommandContext>
{
  [SlashCommand("ping", "Pong!")]
  public static string Ping() => "Pong!";
}
