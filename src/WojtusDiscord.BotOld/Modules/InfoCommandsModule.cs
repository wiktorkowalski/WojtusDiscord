using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace WojtusDiscord.Bot.Modules
{
    public class InfoCommandsModule : ApplicationCommandModule
    {
        [SlashCommand("ping","Ping!")]
        public async Task PingCommand(InteractionContext context)
        {
            await context.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent("Pong!"));
        }
    }
}