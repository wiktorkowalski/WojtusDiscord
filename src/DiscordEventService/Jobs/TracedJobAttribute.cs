using System.Diagnostics;
using Hangfire.Common;
using Hangfire.Server;

namespace DiscordEventService.Jobs;

// One root span per run of the job class that carries this attribute, so the run's database
// and HTTP spans reach Tempo as one trace (TelemetryRegistration.IsForTempo drops them with
// no root).
//
// Only for a job whose run is short and bounded: HealthCheckJob (a fixed set of queries every
// 5 minutes). Not for a backfill or a meme indexing job: one run makes thousands of REST
// calls and would be one trace of thousands of spans.
[AttributeUsage(AttributeTargets.Class)]
internal sealed class TracedJobAttribute : JobFilterAttribute, IServerFilter
{
    public const string SourceName = "DiscordEventService.Jobs";

    private const string ActivityKey = "TracedJob.Activity";

    private static readonly ActivitySource ActivitySource = new(SourceName);

    // Hangfire awaits the job between the two calls on one execution context, so the span
    // started here is Activity.Current inside the job. Null when no trace target is configured.
    public void OnPerforming(PerformingContext context)
    {
        if (ActivitySource.StartActivity($"job {context.BackgroundJob.Job.Type.Name}") is { } activity)
            context.Items[ActivityKey] = activity;
    }

    public void OnPerformed(PerformedContext context)
    {
        if (!context.Items.TryGetValue(ActivityKey, out var item) || item is not Activity activity)
            return;

        // The job logs its own failure, and Hangfire records it: the span only carries the status.
        if (context.Exception is not null)
            activity.SetStatus(ActivityStatusCode.Error);
        activity.Dispose();
    }
}
