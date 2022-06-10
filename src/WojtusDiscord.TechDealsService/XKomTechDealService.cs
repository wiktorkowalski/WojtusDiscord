namespace WojtusDiscord.TechDealsService
{
    public class XKomTechDealService
    {
        private readonly ILogger<XKomTechDealService> _logger;
        private readonly XKomAPIClient _xKomAPIClient;
        private readonly DiscordClient _discordClient;

        public XKomTechDealService(ILogger<XKomTechDealService> logger, XKomAPIClient xKomAPIClient, DiscordClient discordClient)
        {
            _logger = logger;
            _xKomAPIClient = xKomAPIClient;
            _discordClient = discordClient;
        }
        
        public async Task PublishTechDeal()
        {
            _logger.LogInformation("Publishing x-kom deal");
            var xKomDeal = await _xKomAPIClient.GetXKomResponseAsync();
            await _discordClient.SendWebhookMessageAsync($"{xKomDeal.PromotionName} {Environment.NewLine}Stara cena: {xKomDeal.OldPrice} zł{Environment.NewLine}Nowa cena: {xKomDeal.Price}zł{Environment.NewLine}https://www.x-kom.pl/goracy_strzal/");
        }
    }
}
