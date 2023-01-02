using Microsoft.AspNetCore.Authentication.Cookies;
using WojtusDiscord.DashboardBlazorServer.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
    })
    .AddGitHub(options =>
    {
        options.ClientId = "b0fbc8b872c2ae2fa23a";
        options.ClientSecret = "040a1e9716a1e53f4f5d3cdfafc5449da35e39b4";
        options.Scope.Add("user");
    })
    .AddDiscord(options =>
    {
        options.ClientId = "918568639794397214";
        options.ClientSecret = "SfjZ-_432Uil6swDzDJO1a8l-fWDrTyA";
        options.Scope.Add("identify");
        options.Scope.Add("email");
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
