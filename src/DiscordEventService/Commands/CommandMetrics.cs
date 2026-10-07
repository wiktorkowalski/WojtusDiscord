using System.Diagnostics;
using System.Runtime.CompilerServices;
using DiscordEventService.Infrastructure;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Exceptions;
using DSharpPlus.Commands.Processors.SlashCommands;

namespace DiscordEventService.Commands;

// Counts and times every slash command from the two events of the commands extension
// (DiscordClientRegistration), so a command needs no metric code of its own.
//
// A command that catches its own exception and answers the user (MemeCommand does) must say so
// with MarkFailed: the extension saw no error and reports the command as executed.
internal static class CommandMetrics
{
    // The source of the root span of a command (TelemetryRegistration adds it for Tempo).
    public const string SourceName = "DiscordEventService.Commands";

    private static readonly ActivitySource ActivitySource = new(SourceName);

    // Keyed by the context of one execution, and gone with it: nothing to remove.
    private static readonly ConditionalWeakTable<CommandContext, object> Failed = [];
    private static readonly object FailedMark = new();

    // A slash command runs outside any request, so without this span its database and HTTP
    // spans have no root and Tempo drops them (TelemetryRegistration.IsForTempo). The name is
    // the command name only: no argument, no id. Null when no trace target is configured.
    //
    // The command starts it in its own body. The extension counts the command after the body
    // returned (RecordAsync), outside the span: wojtus_command_duration carries no exemplar,
    // and what the command measures inside its body does.
    public static Activity? StartSpan(CommandContext context) =>
        ActivitySource.StartActivity($"command {context.Command.FullName}");

    // For a command that caught its own exception: the execution then counts as "failed".
    public static void MarkFailed(CommandContext context)
    {
        Failed.TryAdd(context, FailedMark);
        Activity.Current?.SetStatus(ActivityStatusCode.Error);
    }

    // Must not throw: it runs inside the extension's own event. An error raised before the
    // extension found the command may come with no command, so the name has a fallback.
    public static Task RecordAsync(CommandContext? context, Exception? exception)
    {
        var outcome = exception is null && context is not null && Failed.TryGetValue(context, out _)
            ? BotMetrics.OutcomeFailed
            : Classify(exception);
        BotMetrics.CommandFinished(context?.Command?.FullName ?? "unknown", outcome, ElapsedSince(context));
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
