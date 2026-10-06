using System.Diagnostics;

namespace DiscordEventService.Infrastructure;

// One Information line per request to the API. "Microsoft.AspNetCore" logs at Warning, so
// without this line no log event of a normal request exists, and a trace has no log to open.
//
// The line is written inside the request activity, and it holds the trace id in its text:
// the console prints the scope (TraceId, SpanId) on a line of its own, and each line is a
// separate entry in Loki.
//
// Only a request that matched an endpoint under /api is logged. That leaves out /metrics and
// /health (probes), /hangfire (its dashboard polls every 2 s), the static files (answered
// before this runs) and the SPA fallback. The route template is logged, never the raw path:
// a path holds ids.
internal sealed class RequestLogMiddleware(RequestDelegate next, ILogger<RequestLogMiddleware> logger)
{
    internal const string NoTrace = "none";

    private const string ApiPrefix = "api";

    public async Task InvokeAsync(HttpContext context)
    {
        // Routing ran before this middleware: the endpoint is known before the request runs.
        if (ApiRouteOf(context.GetEndpoint()) is not { } route)
        {
            await next(context);
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        catch (Exception)
        {
            // No exception handler middleware is registered: the server writes the 500 after
            // this frame, so the status code still reads 200 here.
            Log(context.Request.Method, route, StatusCodes.Status500InternalServerError, startedAt);
            throw;
        }

        Log(context.Request.Method, route, context.Response.StatusCode, startedAt);
    }

    // The route template of an endpoint under /api, with one leading slash, else null. A
    // controller route has no leading slash ("api/guild"), a minimal API route has one.
    internal static string? ApiRouteOf(Endpoint? endpoint)
    {
        if ((endpoint as RouteEndpoint)?.RoutePattern.RawText is not { } template)
            return null;

        // Runs for every routed request, the probes too: nothing is allocated for a route
        // outside /api.
        var trimmed = template.AsSpan().TrimStart('/');
        var isApi = trimmed.Equals(ApiPrefix, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(ApiPrefix + "/", StringComparison.OrdinalIgnoreCase);
        if (!isApi)
            return null;

        return template.Length == trimmed.Length + 1 ? template : string.Concat("/", trimmed);
    }

    private void Log(string method, string route, int statusCode, long startedAt) =>
        logger.LogInformation(
            "HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs} ms, trace {TraceId}",
            method, route, statusCode, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            Activity.Current?.TraceId.ToString() ?? NoTrace);
}
