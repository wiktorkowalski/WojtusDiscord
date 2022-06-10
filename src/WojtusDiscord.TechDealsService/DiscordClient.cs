using Discord.Webhook;

namespace WojtusDiscord.TechDealsService
{
    public class DiscordClient
    {
        public async Task SendWebhookMessageAsync(string message)
        {
            using (var discordClient = new DiscordWebhookClient(Environment.GetEnvironmentVariable("discordWebhook")))
            {
                await discordClient.SendMessageAsync(message);
            }
        }
    }
}