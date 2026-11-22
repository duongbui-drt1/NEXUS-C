using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Nexus.BuildingBlocks.Observability;

public static class NexusPlatformMetrics
{
    public const string MeterName = "Nexus.Platform";
    public const string ActivitySourceName = "Nexus.Platform";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> AuthAttempts = Meter.CreateCounter<long>(
        "nexus.auth.attempts", unit: "{attempt}", description: "Authentication attempts by mode and outcome");

    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>(
        "nexus.messaging.outbox.published", unit: "{message}", description: "Outbox events published");

    public static readonly Counter<long> OutboxFailed = Meter.CreateCounter<long>(
        "nexus.messaging.outbox.failed", unit: "{message}", description: "Outbox publish failures");

    public static readonly Counter<long> DeadLettersCreated = Meter.CreateCounter<long>(
        "nexus.messaging.dlq.created", unit: "{message}", description: "Events routed to dead-letter queue");

    public static readonly Counter<long> DeadLettersRetried = Meter.CreateCounter<long>(
        "nexus.messaging.dlq.retried", unit: "{message}", description: "Dead-letter events manually retried");

    public static readonly Counter<long> WorkflowDecisions = Meter.CreateCounter<long>(
        "nexus.workflow.decisions", unit: "{decision}", description: "Workflow approval/rejection decisions");

    public static readonly Counter<long> WebhookDeliveries = Meter.CreateCounter<long>(
        "nexus.integration.webhook.deliveries", unit: "{delivery}", description: "Webhook delivery attempts by status");

    public static readonly Counter<long> BackgroundJobRuns = Meter.CreateCounter<long>(
        "nexus.jobs.runs", unit: "{run}", description: "Background job executions by job name and outcome");

    public static readonly Histogram<double> BackgroundJobDurationMs = Meter.CreateHistogram<double>(
        "nexus.jobs.duration", unit: "ms", description: "Background job execution duration in milliseconds");
}

public sealed record BackgroundJobStatusDto(
    string JobName,
    string Status,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunCompletedAt,
    long TotalRuns,
    long TotalItemsProcessed,
    long TotalFailures,
    string? LastError);

public interface IBackgroundJobTracker
{
    void RegisterJob(string jobName);
    void RecordRunStarted(string jobName, DateTimeOffset utcNow);
    void RecordRunSucceeded(string jobName, DateTimeOffset utcNow, int itemsProcessed, double durationMs);
    void RecordRunFailed(string jobName, DateTimeOffset utcNow, string error, double durationMs);
    IReadOnlyList<BackgroundJobStatusDto> GetSnapshot();
}

public sealed class BackgroundJobTracker : IBackgroundJobTracker
{
    private sealed class MutableState
    {
        public string JobName { get; init; } = string.Empty;
        public string Status { get; set; } = "Registered";
        public DateTimeOffset? LastRunStartedAt { get; set; }
        public DateTimeOffset? LastRunCompletedAt { get; set; }
        public long TotalRuns { get; set; }
        public long TotalItemsProcessed { get; set; }
        public long TotalFailures { get; set; }
        public string? LastError { get; set; }
        public readonly object Sync = new();
    }

    private readonly ConcurrentDictionary<string, MutableState> _jobs = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterJob(string jobName)
    {
        _jobs.GetOrAdd(jobName, name => new MutableState { JobName = name });
    }

    public void RecordRunStarted(string jobName, DateTimeOffset utcNow)
    {
        var state = _jobs.GetOrAdd(jobName, name => new MutableState { JobName = name });
        lock (state.Sync)
        {
            state.Status = "Running";
            state.LastRunStartedAt = utcNow;
        }
    }

    public void RecordRunSucceeded(string jobName, DateTimeOffset utcNow, int itemsProcessed, double durationMs)
    {
        var state = _jobs.GetOrAdd(jobName, name => new MutableState { JobName = name });
        lock (state.Sync)
        {
            state.Status = "Idle";
            state.LastRunCompletedAt = utcNow;
            state.TotalRuns++;
            state.TotalItemsProcessed += itemsProcessed;
            state.LastError = null;
        }

        NexusPlatformMetrics.BackgroundJobRuns.Add(1,
            new KeyValuePair<string, object?>("job", jobName),
            new KeyValuePair<string, object?>("result", "success"));
        NexusPlatformMetrics.BackgroundJobDurationMs.Record(durationMs,
            new KeyValuePair<string, object?>("job", jobName));
    }

    public void RecordRunFailed(string jobName, DateTimeOffset utcNow, string error, double durationMs)
    {
        var state = _jobs.GetOrAdd(jobName, name => new MutableState { JobName = name });
        var redacted = error.Length > 400 ? error[..400] : error;
        lock (state.Sync)
        {
            state.Status = "Degraded";
            state.LastRunCompletedAt = utcNow;
            state.TotalRuns++;
            state.TotalFailures++;
            state.LastError = redacted;
        }

        NexusPlatformMetrics.BackgroundJobRuns.Add(1,
            new KeyValuePair<string, object?>("job", jobName),
            new KeyValuePair<string, object?>("result", "failure"));
        NexusPlatformMetrics.BackgroundJobDurationMs.Record(durationMs,
            new KeyValuePair<string, object?>("job", jobName));
    }

    public IReadOnlyList<BackgroundJobStatusDto> GetSnapshot()
    {
        var list = new List<BackgroundJobStatusDto>();
        foreach (var state in _jobs.Values.OrderBy(j => j.JobName))
        {
            lock (state.Sync)
            {
                list.Add(new BackgroundJobStatusDto(
                    state.JobName,
                    state.Status,
                    state.LastRunStartedAt,
                    state.LastRunCompletedAt,
                    state.TotalRuns,
                    state.TotalItemsProcessed,
                    state.TotalFailures,
                    state.LastError));
            }
        }

        return list;
    }
}
