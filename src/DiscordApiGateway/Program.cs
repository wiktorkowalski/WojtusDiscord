using DiscordApiGateway.Options;
using DiscordApiGateway.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables();

builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.Prefix));

// Add services to the container.
builder.Services.AddHostedService<DiscordApiService>();
builder.Services.AddSingleton<DiscordApiService>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
