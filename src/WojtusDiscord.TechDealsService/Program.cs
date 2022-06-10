using WojtusDiscord.TechDealsService;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<XKomAPIClient>();
builder.Services.AddSingleton<DiscordClient>();
builder.Services.AddSingleton<XKomTechDealService>();
builder.Services.AddCronJob<XKomCronJobService>(config =>
{
    config.CronExpression = "1 10,22 * * *";
    config.TimeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
});

var app = builder.Build();

app.MapGet("/xkom", async (XKomTechDealService xKomTechDealService, HttpContext context) =>
{
    await xKomTechDealService.PublishTechDeal();
    return Results.Ok();
}).WithName("Xkom Tech Deal");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();
