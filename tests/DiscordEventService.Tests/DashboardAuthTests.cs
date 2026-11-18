using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using DiscordEventService.Configuration;
using DiscordEventService.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DiscordEventService.Tests;

// #339: the gate in Discord mode, on stub endpoints shaped like Program.cs. The OAuth round trip
// itself is not run; /test/sign-in issues the same cookie the Discord callback would.
public sealed class DashboardAuthTests
{
    private const ulong AdminId = 100;
    private const ulong MemberId = 200;
    private const ulong OutsiderId = 300;
    private const string OpsKey = "test-ops-key-0123456789";

    [Fact]
    public async Task NoCookie_ReadApi_Is401Json_NotRedirect()
    {
        await using var host = await StartAsync();
        var response = await host.Client.GetAsync("/api/people");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NoCookie_Hangfire_RedirectsToLogin()
    {
        await using var host = await StartAsync();
        var response = await host.Client.GetAsync("/hangfire");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/auth/login?returnUrl=", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Anonymous_HealthMetricsMeAndShell_AreReachable()
    {
        await using var host = await StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/metrics")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/some/spa/route")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Member_ReadsApi_ButOps_Is403()
    {
        await using var host = await StartAsync();
        var cookie = await host.SignInAsync(MemberId, member: true);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, "/api/people", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Post, "/api/ops/thing", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, "/hangfire", cookie)).StatusCode);
    }

    [Fact]
    public async Task NonMember_ReadApi_Is403()
    {
        await using var host = await StartAsync();
        var cookie = await host.SignInAsync(OutsiderId, member: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, "/api/people", cookie)).StatusCode);
    }

    [Fact]
    public async Task AdminId_ReachesEverything_WithoutGuildMembership()
    {
        await using var host = await StartAsync();
        var cookie = await host.SignInAsync(AdminId, member: false);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, "/api/people", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Post, "/api/ops/thing", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, "/hangfire", cookie)).StatusCode);
    }

    [Fact]
    public async Task OpsKey_OnlyOnAdminEndpoints()
    {
        await using var host = await StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Post, "/api/ops/thing", bearer: OpsKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, "/api/people", bearer: OpsKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await host.SendAsync(HttpMethod.Get, "/hangfire", bearer: OpsKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Post, "/api/ops/thing", bearer: "wrong")).StatusCode);
    }

    [Fact]
    public async Task EmptyOpsKey_NeverMatches()
    {
        await using var host = await StartAsync(opsKey: "");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Post, "/api/ops/thing", bearer: "anything")).StatusCode);
    }

    [Fact]
    public async Task StaleCookie_LeaverLosesAccess_UnknownKeepsIt()
    {
        var checker = new FakeChecker();
        await using var host = await StartAsync(checker: checker);
        var stale = DateTimeOffset.UtcNow.AddHours(-2);
        var cookie = await host.SignInAsync(MemberId, member: true, checkedAt: stale);

        checker.Status = MembershipStatus.Unknown;
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, "/api/people", cookie)).StatusCode);

        checker.Status = MembershipStatus.NotMember;
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, "/api/people", cookie)).StatusCode);
    }

    [Theory]
    [InlineData("/memes", true)]
    [InlineData("/hangfire/jobs?x=1", true)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("/\t/evil.example", false)]
    public void IsLocalUrl_RejectsOpenRedirects(string url, bool expected) =>
        Assert.Equal(expected, DashboardAuth.IsLocalUrl(url));

    [Fact]
    public void Disabled_OutsideDevelopment_RefusesToStart()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DashboardAuth.Validate(
            new AuthOptions { Mode = AuthMode.Disabled }, new TestHostEnvironment { EnvironmentName = "Production" }));
        Assert.Contains("Development", ex.Message);
    }

    [Fact]
    public void Disabled_InDevelopment_Starts() =>
        DashboardAuth.Validate(new AuthOptions { Mode = AuthMode.Disabled }, new TestHostEnvironment { EnvironmentName = "Development" });

    [Fact]
    public void Discord_WithoutClientConfig_RefusesToStart()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DashboardAuth.Validate(
            new AuthOptions { Mode = AuthMode.Discord }, new TestHostEnvironment { EnvironmentName = "Production" }));
        Assert.Contains("Auth:GuildId", ex.Message);
        Assert.Contains("Auth:Discord:ClientSecret", ex.Message);
    }

    private static async Task<AuthHost> StartAsync(string opsKey = OpsKey, FakeChecker? checker = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Discord",
            ["Auth:GuildId"] = "1",
            ["Auth:AdminUserIds:0"] = AdminId.ToString(),
            ["Auth:OpsApiKey"] = opsKey,
            ["Auth:Discord:ClientId"] = "client",
            ["Auth:Discord:ClientSecret"] = "secret",
        });
        builder.Services.AddDashboardAuth(builder.Configuration, new TestHostEnvironment { EnvironmentName = "Production" });
        builder.Services.AddSingleton<IGuildMembershipChecker>(checker ?? new FakeChecker());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAuthEndpoints();
        app.MapGet("/api/people", () => "ok");
        app.MapPost("/api/ops/thing", () => "ok").RequireAuthorization(DashboardAuth.AdminPolicy);
        app.MapGet("/hangfire", () => "ok").RequireAuthorization(DashboardAuth.HangfireAdminPolicy);
        app.MapGet("/health", () => "ok").AllowAnonymous();
        app.MapGet("/metrics", () => "ok").AllowAnonymous();
        app.MapFallback(() => "shell").AllowAnonymous();
        app.MapPost("/test/sign-in", async (HttpContext ctx, ulong id, bool member, long checkedAt) =>
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                new Claim(ClaimTypes.Name, $"user{id}"),
                new Claim(DashboardAuth.MemberClaim, member ? "true" : "false"),
                new Claim(DashboardAuth.MemberCheckedAtClaim, checkedAt.ToString()),
            ], "test");
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }).AllowAnonymous();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        // Secure cookies are never sent over http by a CookieContainer, so the test carries them by hand.
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(address),
        };
        return new AuthHost(app, client);
    }

    private sealed class FakeChecker : IGuildMembershipChecker
    {
        public MembershipStatus Status { get; set; } = MembershipStatus.Member;
        public Task<MembershipStatus> CheckAsync(ulong userId, CancellationToken cancellationToken) => Task.FromResult(Status);
    }

    private sealed class AuthHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public async Task<string> SignInAsync(ulong id, bool member, DateTimeOffset? checkedAt = null)
        {
            var at = (checkedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            var response = await client.PostAsync($"/test/sign-in?id={id}&member={member}&checkedAt={at}", null);
            response.EnsureSuccessStatusCode();
            return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        }

        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? cookie = null, string? bearer = null)
        {
            var request = new HttpRequestMessage(method, path);
            if (cookie is not null)
                request.Headers.Add("Cookie", cookie);
            if (bearer is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            return client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }
}
