using Serilog;
using Serilog.Templates;

namespace DiscordEventService.Infrastructure;

// One JSON object per line on stdout, for Loki's `| json`. Serilog is only the formatter and
// sink: it is added as a provider to the host's and the DSharpPlus container's logger factories,
// so their filters (Logging:LogLevel, the shutdown filter) and LogEventCounterProvider stay as they
// are. Its own minimum is open for the same reason. Level is written for every event, Information
// too, and an exception stays inside its line. Trace and span ids come from Activity.Current.
internal static class JsonConsoleLog
{
    internal const string Template =
        "{ {time: @t, level: @l, category: SourceContext, msg: @m, exception: @x, trace_id: @tr, span_id: @sp, ..rest()} }\n";

    public static Serilog.Core.Logger Create() => new LoggerConfiguration()
        .MinimumLevel.Verbose()
        .Enrich.FromLogContext()
        .WriteTo.Console(new ExpressionTemplate(Template))
        .CreateLogger();
}
