using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using AspNet.Security.OAuth.Discord;
using DiscordEventService.Configuration;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Infrastructure.Auth;

// Login gate of the dashboard, /api and /hangfire (#328 is the spec, #339 the issue).
// Member = in Auth:GuildId (dashboard + read API, the fallback policy). Admin = id in
// Auth:AdminUserIds, or the ops Bearer key (ops endpoints). HangfireAdmin = Admin by cookie only.
internal static class DashboardAuth
{
    public const string MemberPolicy = "Member";
    public const string AdminPolicy = "Admin";
    public const string HangfireAdminPolicy = "HangfireAdmin";

    private const string OpsKeyScheme = "OpsKey";
    private const string DisabledScheme = "Disabled";

    internal const string MemberClaim = "wojtus:member";
    internal const string MemberCheckedAtClaim = "wojtus:member_checked_at";
    // The ops key and Disabled mode act as Admin without a Discord user behind them.
    private const string SuperuserClaim = "wojtus:superuser";

    private static readonly TimeSpan MemberRecheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(30);

    public static void AddDashboardAuth(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(AuthOptions.SectionName);
        services.AddOptions<AuthOptions>().Bind(section);
        var options = section.Get<AuthOptions>() ?? new AuthOptions();
        Validate(options, environment);

        services.AddSingleton<IGuildMembershipChecker, DiscordGuildMembershipChecker>();

        // Traefik terminates TLS: without the forwarded proto, redirect_uri would be http://.
        // Trusting any proxy is safe here: the container publishes no port, only the docker
        // networks reach it.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        string[] cookieSchemes;
        string[] adminSchemes;
        if (options.Mode == AuthMode.Disabled)
        {
            services.AddAuthentication(DisabledScheme)
                .AddScheme<AuthenticationSchemeOptions, DisabledAuthHandler>(DisabledScheme, null);
            cookieSchemes = adminSchemes = [DisabledScheme];
        }
        else
        {
            AddDiscordSchemes(services, options);
            cookieSchemes = [CookieAuthenticationDefaults.AuthenticationScheme];
            adminSchemes = [CookieAuthenticationDefaults.AuthenticationScheme, OpsKeyScheme];
        }

        var member = new AuthorizationPolicyBuilder(cookieSchemes)
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => IsAdmin(ctx.User, options) || ctx.User.HasClaim(MemberClaim, "true"))
            .Build();

        services.AddAuthorizationBuilder()
            .AddPolicy(MemberPolicy, member)
            .SetFallbackPolicy(member)
            .AddPolicy(AdminPolicy, p => p
                .AddAuthenticationSchemes(adminSchemes)
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => IsAdmin(ctx.User, options)))
            // #328: the ops key is never accepted on /hangfire; GET /api/ops/jobs replaces it.
            .AddPolicy(HangfireAdminPolicy, p => p
                .AddAuthenticationSchemes(cookieSchemes)
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => IsAdmin(ctx.User, options)));
    }

    // Startup guard (#328): Disabled must never reach prod; Discord needs its whole config.
    internal static void Validate(AuthOptions options, IHostEnvironment environment)
    {
        if (options.Mode == AuthMode.Disabled)
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException(
                    $"Auth:Mode=Disabled is allowed only in Development (environment: {environment.EnvironmentName}). Set Auth:Mode=Discord.");
            return;
        }

        List<string> missing = [];
        if (options.GuildId == 0) missing.Add("Auth:GuildId");
        if (string.IsNullOrWhiteSpace(options.Discord.ClientId)) missing.Add("Auth:Discord:ClientId");
        if (string.IsNullOrWhiteSpace(options.Discord.ClientSecret)) missing.Add("Auth:Discord:ClientSecret");
        if (missing.Count > 0)
            throw new InvalidOperationException($"Auth:Mode=Discord needs {string.Join(", ", missing)}.");
    }

    private static void AddDiscordSchemes(IServiceCollection services, AuthOptions options)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "wojtus_auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.ExpireTimeSpan = CookieLifetime;
                o.SlidingExpiration = true;
                o.LoginPath = "/auth/login";
                o.ReturnUrlParameter = "returnUrl";
                // The API answers 401/403 as JSON status codes, never a 302 to the login page.
                o.Events.OnRedirectToLogin = ctx => ApiAwareRedirect(ctx, StatusCodes.Status401Unauthorized);
                // No access-denied page exists: a plain 403 everywhere (the SPA shows its own screen).
                o.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                o.Events.OnValidatePrincipal = RecheckMembershipAsync;
            })
            .AddDiscord(o =>
            {
                o.ClientId = options.Discord.ClientId!;
                o.ClientSecret = options.Discord.ClientSecret!;
                o.CallbackPath = "/auth/callback";
                o.Scope.Clear();
                o.Scope.Add("identify");
                o.SaveTokens = false;
                o.Events.OnCreatingTicket = async ctx =>
                {
                    var userId = UserId(ctx.Principal!);
                    var checker = ctx.HttpContext.RequestServices.GetRequiredService<IGuildMembershipChecker>();
                    // Fail closed at login: Unknown is not a member.
                    var status = userId is { } id
                        ? await checker.CheckAsync(id, ctx.HttpContext.RequestAborted)
                        : MembershipStatus.Unknown;
                    var identity = (ClaimsIdentity)ctx.Principal!.Identity!;
                    identity.AddClaim(new Claim(MemberClaim, status == MembershipStatus.Member ? "true" : "false"));
                    identity.AddClaim(new Claim(MemberCheckedAtClaim, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
                };
            })
            .AddScheme<AuthenticationSchemeOptions, OpsKeyAuthHandler>(OpsKeyScheme, null);
    }

    private static Task ApiAwareRedirect(RedirectContext<CookieAuthenticationOptions> ctx, int statusCode)
    {
        if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/auth"))
            ctx.Response.StatusCode = statusCode;
        else
            ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }

    // A leaver loses access within an hour. Unknown (bot restarting) keeps the old answer and
    // retries on the next request, so a bot restart does not log everyone out.
    private static async Task RecheckMembershipAsync(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal!;
        var checkedAt = long.TryParse(principal.FindFirstValue(MemberCheckedAtClaim), CultureInfo.InvariantCulture, out var s)
            ? DateTimeOffset.FromUnixTimeSeconds(s)
            : DateTimeOffset.MinValue;
        if (DateTimeOffset.UtcNow - checkedAt < MemberRecheckInterval || UserId(principal) is not { } userId)
            return;

        var checker = ctx.HttpContext.RequestServices.GetRequiredService<IGuildMembershipChecker>();
        var status = await checker.CheckAsync(userId, ctx.HttpContext.RequestAborted);
        if (status == MembershipStatus.Unknown)
            return;

        var identity = new ClaimsIdentity(
            principal.Claims.Where(c => c.Type is not (MemberClaim or MemberCheckedAtClaim)),
            principal.Identity!.AuthenticationType);
        identity.AddClaim(new Claim(MemberClaim, status == MembershipStatus.Member ? "true" : "false"));
        identity.AddClaim(new Claim(MemberCheckedAtClaim, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        ctx.ReplacePrincipal(new ClaimsPrincipal(identity));
        ctx.ShouldRenew = true;
    }

    private static ulong? UserId(ClaimsPrincipal principal) =>
        ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out var id) ? id : null;

    // Evaluated per request from options, never baked into the cookie.
    private static bool IsAdmin(ClaimsPrincipal user, AuthOptions options) =>
        user.HasClaim(SuperuserClaim, "true") || UserId(user) is { } id && options.IsAdmin(id);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth").AllowAnonymous();

        group.MapGet("/login", (string? returnUrl, HttpContext ctx) =>
        {
            // Only local paths: an absolute returnUrl would be an open redirect.
            var target = !string.IsNullOrEmpty(returnUrl) && IsLocalUrl(returnUrl) ? returnUrl : "/";
            return ctx.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.Mode == AuthMode.Disabled
                ? Results.LocalRedirect(target)
                : Results.Challenge(new AuthenticationProperties { RedirectUri = target }, [DiscordAuthenticationDefaults.AuthenticationScheme]);
        });

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            if (ctx.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.Mode == AuthMode.Discord)
                await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        group.MapGet("/me", async (HttpContext ctx, IOptions<AuthOptions> options) =>
        {
            var result = await ctx.AuthenticateAsync();
            if (!result.Succeeded)
                return Results.Unauthorized();

            var user = result.Principal;
            var isAdmin = IsAdmin(user, options.Value);
            return Results.Ok(new AuthMeResponse(
                UserId(user)?.ToString(CultureInfo.InvariantCulture),
                user.FindFirstValue(ClaimTypes.Name),
                user.FindFirstValue(DiscordAuthenticationConstants.Claims.AvatarHash),
                isAdmin || user.HasClaim(MemberClaim, "true"),
                isAdmin));
        });
    }

    // Browsers drop tabs/newlines, so "/\t/evil" would become "//evil": control chars are rejected.
    internal static bool IsLocalUrl(string url) =>
        url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
        && !url.Any(char.IsControl);

    internal sealed record AuthMeResponse(string? Id, string? Username, string? AvatarHash, bool IsMember, bool IsAdmin);

    // `Authorization: Bearer <Auth:OpsApiKey>`. Registered only in Discord mode, and only the
    // Admin policy names it.
    private sealed class OpsKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<AuthOptions> authOptions)
        : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var key = authOptions.Value.OpsApiKey;
            var header = Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(key) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());

            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header["Bearer ".Length..]), Encoding.UTF8.GetBytes(key)))
                return Task.FromResult(AuthenticateResult.Fail("Invalid ops key"));

            var identity = new ClaimsIdentity([new Claim(SuperuserClaim, "true"), new Claim(ClaimTypes.Name, "ops-key")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    // Development only (Validate enforces it): everyone is a fake Admin.
    private sealed class DisabledAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(SuperuserClaim, "true"), new Claim(ClaimTypes.Name, "dev (auth disabled)")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

// The real gate is RequireAuthorization(HangfireAdminPolicy) on the endpoint; Hangfire's own
// default filter allows only local requests, which behind Traefik means nobody.
internal sealed class EndpointAuthorizedDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
