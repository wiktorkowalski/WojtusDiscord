using Microsoft.EntityFrameworkCore;
using Serilog;
using WojtusDiscord.ArchiveService;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .WriteTo.Console()
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
);

// Add services to the container.
builder.Services.AddDbContext<DatabaseContext>(options =>
{
    options.UseNpgsql(Environment.GetEnvironmentVariable("connectionString"));
});
builder.Services.AddSingleton<DatabaseProvider>();
builder.Services.AddSingleton<DiscordEventsHandlers>();
builder.Services.AddHostedService<DiscordService>();

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

app.UseAuthorization();

app.MapControllers();

app.Run();
