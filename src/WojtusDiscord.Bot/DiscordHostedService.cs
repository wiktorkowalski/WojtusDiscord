using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Serilog;

namespace WojtusDiscord.Bot
{
    internal class DiscordHostedService : IHostedService
    {
        private readonly ILogger<DiscordHostedService> _logger;
        private readonly DiscordClient _discordClient;

        public DiscordHostedService(ILogger<DiscordHostedService> logger)
        {
            _logger = logger;
            _discordClient = new DiscordClient(GetDiscordConfiguration());
        }

        private DiscordConfiguration GetDiscordConfiguration()
        {
            string token = "";
            var region = Amazon.RegionEndpoint.EUCentral1;
            var request = new GetParameterRequest()
            {
                Name = "/wojtus/prod/discordtoken"
            };
            using (var client = new AmazonSimpleSystemsManagementClient(region))
            {
                try
                {
                    var response = client.GetParameterAsync(request).Result;
                    token = response.Parameter.Value;
                    _logger.LogInformation($"Parameter {request.Name} has value: {response.Parameter.Value}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error occurred: {ex.Message}");
                }
            }

            return new DiscordConfiguration
            {
                Token = token,//"ODU5NDc4NTY3MTI5MzE3Mzg2.YNtRyg.NuKdxRbojzZcwaBQSLg1Wpjca5U",//EnvReader.GetStringValue("discord-token"),
                TokenType = TokenType.Bot,
                Intents = DiscordIntents.All,
                LoggerFactory = new LoggerFactory().AddSerilog()
            };
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting DiscordService");

            _discordClient.MessageCreated += MessageCreated;

            return _discordClient.ConnectAsync();
        }

        private Task MessageCreated(DiscordClient sender, MessageCreateEventArgs e)
        {
            _logger.LogInformation($"Message received: [{e.Author.Username}]: {e.Message.Content}");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping DiscordService");
            return _discordClient.DisconnectAsync();
        }
    }
}
