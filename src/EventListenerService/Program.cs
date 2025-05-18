using System.ClientModel;
using EventListenerService.Data;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using EventListenerService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
.AddJsonOptions(options => 
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services
    .AddDiscordGateway(options =>
    {
        options.Token = builder.Configuration["Discord:BotToken"];
        options.Intents = GatewayIntents.All;
        options.Presence = new PresenceProperties(UserStatusType.Online);
    })
    .AddGatewayEventHandlers(typeof(Program).Assembly)
    .AddApplicationCommands();

// builder.Services.AddDiscordClient(builder.Configuration["Discord:BotToken"] ?? throw new ArgumentNullException(), DiscordIntents.All);

builder.Services.AddDbContext<WojtusContext>(options =>
    options
    .UseNpgsql(builder.Configuration.GetConnectionString("WojtusDatabase"))
    .UseSnakeCaseNamingConvention());

 // Register OpenAIClient for OpenRouter
builder.Services.AddScoped(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var apiKey = configuration["OpenRouter:ApiKey"];
    var baseUrl = configuration["OpenRouter:BaseUrl"];
    var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions()
    {
        Endpoint = new Uri(baseUrl),
    });
    return client;
});

builder.Services.AddScoped<IMemeMetadataGenerationService, MemeMetadataGenerationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.AddModules(typeof(Program).Assembly);
app.UseGatewayEventHandlers();

app.MapControllers();

app.Run();
