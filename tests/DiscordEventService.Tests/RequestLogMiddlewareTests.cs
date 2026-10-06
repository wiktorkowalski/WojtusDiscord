using DiscordEventService.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DiscordEventService.Tests;

// The request log is what a trace in Grafana opens: one Information line per API request,
// with the route template, and no line for the probes, the Hangfire dashboard or the SPA.
public sealed class RequestLogMiddlewareTests
{
    [Theory]
    [InlineData("api/guild", "/api/guild")]
    [InlineData("api/stats/memes/thumbnails/{attachmentDiscordId}", "/api/stats/memes/thumbnails/{attachmentDiscordId}")]
    [InlineData("/api/backfill/{guildId}", "/api/backfill/{guildId}")]
    public async Task ApiEndpoint_LogsOneLineWithMethodRouteTemplateAndStatus(string template, string loggedRoute)
    {
        var log = new RecordingLogger();
        var context = NewContext(template);

        await NewMiddleware(log, http =>
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }).InvokeAsync(context);

        var (level, message) = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.StartsWith($"HTTP GET {loggedRoute} responded 404 in ", message);
        // No request activity in a unit test; the live boot check covers the real trace id.
        Assert.EndsWith($"trace {RequestLogMiddleware.NoTrace}", message);
    }

    [Theory]
    [InlineData("/metrics")]
    [InlineData("/health")]
    [InlineData("/hangfire/{**path}")]
    [InlineData("{*path:nonfile}")]
    [InlineData("apidocs")]
    public async Task OtherEndpoint_LogsNothing(string template)
    {
        var log = new RecordingLogger();

        await NewMiddleware(log, _ => Task.CompletedTask).InvokeAsync(NewContext(template));

        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task NoEndpoint_LogsNothing()
    {
        var log = new RecordingLogger();

        await NewMiddleware(log, _ => Task.CompletedTask).InvokeAsync(new DefaultHttpContext());

        Assert.Empty(log.Entries);
    }

    // No exception handler is registered, so the status code is still 200 when the exception
    // passes the middleware. The line must say 500, and the exception must go on.
    [Fact]
    public async Task ThrowingEndpoint_LogsStatus500AndRethrows()
    {
        var log = new RecordingLogger();
        var middleware = NewMiddleware(log, _ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(NewContext("api/guild")));

        Assert.StartsWith("HTTP GET /api/guild responded 500 in ", Assert.Single(log.Entries).Message);
    }

    private static RequestLogMiddleware NewMiddleware(RecordingLogger log, RequestDelegate next) =>
        new(next, log.For<RequestLogMiddleware>());

    private static DefaultHttpContext NewContext(string routeTemplate)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask, RoutePatternFactory.Parse(routeTemplate), order: 0, metadata: null, displayName: routeTemplate));
        return context;
    }
}
