using DiscordEventService.Infrastructure;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Exceptions;
using DSharpPlus.Commands.Processors.SlashCommands;

namespace DiscordEventService.Commands;

// Counts and times every slash command from the two events of the commands extension
// (DiscordClientRegistration), so a command needs no metric code of its own.
//
// A command that catches its own exception and answers the user (MemeCommand does) ends as
// "ok" here: the extension saw no error. Its failure is in wojtus_log_events_total{level="error"}.
internal static class CommandMetrics
{
    // Must not throw: it runs inside the extension's own event. An error raised before the
    // extension found the command may come with no command, so the name has a fallback.
    public static Task RecordAsync(CommandContext? context, Exception? exception)
    {
        BotMetrics.CommandFinished(context?.Command?.FullName ?? "unknown", Classify(exception), ElapsedSince(context));
        return Task.CompletedTask;
    }

    // A fixed set of outcomes from the exception type. Never the message: it is free text.
    internal static string Classify(Exception? exception) => exception switch
    {
        null => BotMetrics.OutcomeOk,
        ChecksFailedException or ParameterChecksFailedException => "check_failed",
        ArgumentParseException => "bad_argument",
        CommandNotExecutableException => "not_executable",
        OperationCanceledException => "cancelled",
        _ => BotMetrics.OutcomeFailed,
    };

    // The extension gives no start time. For a slash command the interaction id holds the
    // instant Discord made it, so the time includes the way from Discord to the bot. A clock
    // that runs behind Discord's must not give a negative time.
    private static TimeSpan ElapsedSince(CommandContext? context)
    {
        if (context is not SlashCommandContext slash)
            return TimeSpan.Zero;

        var elapsed = DateTimeOffset.UtcNow - slash.Interaction.CreationTimestamp;
        return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
    }
}
