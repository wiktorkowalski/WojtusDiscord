namespace WojtusDiscord.TechDealsService
{
    public class XKomCronJobService : CronJobService
    {
        private readonly ILogger<XKomCronJobService> _logger;
        private readonly XKomTechDealService _xKomTechDealService;

        public XKomCronJobService(IScheduleConfig<XKomCronJobService> config, ILogger<XKomCronJobService> logger, XKomTechDealService xKomTechDealService)
        : base(config.CronExpression, config.TimeZoneInfo)
        {
            _logger = logger;
            _xKomTechDealService = xKomTechDealService;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("XKomDealService starts.");
            return base.StartAsync(cancellationToken);
        }

        public override async Task DoWork(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"{DateTime.Now:hh:mm:ss} XKomDealService is working.");
            _xKomTechDealService.PublishTechDeal();
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("XKomDealService is stopping.");
            return base.StopAsync(cancellationToken);
        }
    }
}
