namespace DiscordEventService.Jobs;

internal static class HealthCheckRegistration
{
    // The alert webhook is a Discord webhook URL: its token is a segment of the path. The
    // default loggers of a client write the URL twice, in "Sending HTTP request POST <url>" and
    // in a scope "HTTP POST <url>" that the console prints under every line logged inside the
    // call, a Warning too. So a log level cannot hide it: this client gets no logger at all.
    // HealthCheckJob logs a send that failed, and counts every send.
    public static IServiceCollection AddHealthCheckWebhookClient(this IServiceCollection services)
    {
        services.AddHttpClient(HealthCheckJob.WebhookHttpClientName).RemoveAllLoggers();
        return services;
    }
}
