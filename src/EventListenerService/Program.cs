using EventListenerService.Data;
using Microsoft.EntityFrameworkCore;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services
    .AddDiscordGateway(options =>
    {
        options.Token = builder.Configuration["Discord:BotToken"];
        options.Intents = GatewayIntents.All;
    })
    .AddGatewayEventHandlers(typeof(Program).Assembly)
    .AddApplicationCommands();

builder.Services.AddDbContext<WojtusContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("WojtusDatabase")));

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
