using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using PlaceContext.Application.Observability;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Observability;

/// <summary>
/// In-process OpenTelemetry reader for the jobs pipeline: an <see cref="ActivityListener"/> over the
/// <c>PlaceContext.Jobs</c> ActivitySource captures recent run/shard traces into a bounded ring
/// buffer, and a <see cref="MeterListener"/> over the same-named Meter aggregates counters and
/// histogram summaries — so the portal can show live jobs telemetry with no external OTel collector.
///
/// A singleton whose listeners are wired up in the constructor; <see cref="JobTelemetryCollectorStartup"/>
/// (a no-op <see cref="Microsoft.Extensions.Hosting.IHostedService"/>) forces DI to construct it at
/// Host startup rather than on first page hit, so nothing from an early run is missed.
///
/// Shard activities are children of their run's activity and both carry a <c>run.id</c> tag, but the
/// run's outer span (see RunJobHandler) doesn't stop until well after every shard has — so shard
/// captures are buffered per run-id until the parent "job.run" activity stops, then attached and
/// flushed together as one <see cref="JobRunTelemetry"/>.
/// </summary>
public sealed class JobTelemetryCollector : IJobTelemetryReader, IDisposable
{
    private const int MaxRuns = 200;

    private readonly object _runsGate = new();
    private readonly LinkedList<JobRunTelemetry> _runs = new(); // newest at the front

    private readonly ConcurrentDictionary<Guid, ConcurrentBag<ShardTelemetry>> _pendingShards = new();

    private long _runsStarted;
    private readonly ConcurrentDictionary<string, long> _runsCompletedByStatus = new();
    private readonly ConcurrentDictionary<string, long> _shardsCompletedByOutcome = new();
    private readonly DurationAccumulator _runDuration = new();
    private readonly DurationAccumulator _shardDuration = new();

    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    public JobTelemetryCollector()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == JobTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnActivityStopped,
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == JobTelemetry.SourceName)
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        _meterListener.SetMeasurementEventCallback<long>(OnLongMeasurement);
        _meterListener.SetMeasurementEventCallback<double>(OnDoubleMeasurement);
        _meterListener.Start();
    }

    // ── ActivityListener: traces ─────────────────────────────────────────────────────────────────

    private void OnActivityStopped(Activity activity)
    {
        switch (activity.OperationName)
        {
            case "job.shard": CaptureShard(activity); break;
            case "job.run": CaptureRun(activity); break;
        }
    }

    private void CaptureShard(Activity activity)
    {
        var runId = TagGuid(activity, "run.id");
        if (runId is null) return; // can't correlate to a run — drop rather than guess

        var index = int.TryParse(TagString(activity, "shard.index"), out var i) ? i : 0;
        var outcome = TagString(activity, "shard.outcome");
        var exitCode = int.TryParse(TagString(activity, "shard.exit_code"), out var ec) ? ec : (int?)null;
        var shard = new ShardTelemetry(index, outcome, exitCode, activity.Duration.TotalMilliseconds);
        _pendingShards.GetOrAdd(runId.Value, static _ => new ConcurrentBag<ShardTelemetry>()).Add(shard);
    }

    private void CaptureRun(Activity activity)
    {
        var runId = TagGuid(activity, "run.id") ?? Guid.Empty;
        var jobId = TagGuid(activity, "job.id") ?? Guid.Empty;
        var jobName = TagString(activity, "job.name");
        var projectId = TagGuid(activity, "project.id");
        var status = TagString(activity, "run.status");
        var replay = bool.TryParse(TagString(activity, "job.replay"), out var r) && r;

        IReadOnlyList<ShardTelemetry> shards = Array.Empty<ShardTelemetry>();
        if (runId != Guid.Empty && _pendingShards.TryRemove(runId, out var bag))
            shards = bag.OrderBy(s => s.Index).ToList();

        var telemetry = new JobRunTelemetry(
            runId, jobId, jobName, projectId, status, replay,
            new DateTimeOffset(activity.StartTimeUtc, TimeSpan.Zero),
            activity.Duration.TotalMilliseconds, shards);

        lock (_runsGate)
        {
            _runs.AddFirst(telemetry);
            while (_runs.Count > MaxRuns) _runs.RemoveLast();
        }
    }

    private static Guid? TagGuid(Activity activity, string key)
        => Guid.TryParse(activity.GetTagItem(key)?.ToString(), out var g) ? g : null;

    private static string? TagString(Activity activity, string key) => activity.GetTagItem(key)?.ToString();

    // ── MeterListener: metrics ───────────────────────────────────────────────────────────────────

    private void OnLongMeasurement(Instrument instrument, long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        switch (instrument.Name)
        {
            case "placecontext.jobs.runs.started":
                Interlocked.Add(ref _runsStarted, measurement);
                break;
            case "placecontext.jobs.runs.completed":
                Increment(_runsCompletedByStatus, TagValue(tags, "status"), measurement);
                break;
            case "placecontext.jobs.shards.completed":
                Increment(_shardsCompletedByOutcome, TagValue(tags, "outcome"), measurement);
                break;
        }
    }

    private void OnDoubleMeasurement(Instrument instrument, double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        switch (instrument.Name)
        {
            case "placecontext.jobs.run.duration": _runDuration.Record(measurement); break;
            case "placecontext.jobs.shard.duration": _shardDuration.Record(measurement); break;
        }
    }

    private static string TagValue(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var t in tags)
            if (t.Key == key) return t.Value?.ToString() ?? "unknown";
        return "unknown";
    }

    private static void Increment(ConcurrentDictionary<string, long> dict, string key, long amount)
        => dict.AddOrUpdate(key, amount, (_, current) => current + amount);

    // ── IJobTelemetryReader ───────────────────────────────────────────────────────────────────────

    public JobTelemetrySnapshot Snapshot() => new(
        Interlocked.Read(ref _runsStarted),
        new Dictionary<string, long>(_runsCompletedByStatus),
        new Dictionary<string, long>(_shardsCompletedByOutcome),
        _runDuration.ToSummary(),
        _shardDuration.ToSummary());

    public IReadOnlyList<JobRunTelemetry> RecentRuns(int take = 50)
    {
        lock (_runsGate) return _runs.Take(take).ToList();
    }

    public IReadOnlyList<JobRunTelemetry> RunsForJob(Guid jobId, int take = 20)
    {
        lock (_runsGate) return _runs.Where(r => r.JobId == jobId).Take(take).ToList();
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }
}

/// <summary>Thread-safe count/sum/min/max accumulator backing a <see cref="DurationSummary"/>.</summary>
internal sealed class DurationAccumulator
{
    private readonly object _gate = new();
    private long _count;
    private double _sum;
    private double _min = double.PositiveInfinity;
    private double _max = double.NegativeInfinity;

    public void Record(double value)
    {
        lock (_gate)
        {
            _count++;
            _sum += value;
            if (value < _min) _min = value;
            if (value > _max) _max = value;
        }
    }

    public DurationSummary? ToSummary()
    {
        lock (_gate)
            return _count == 0 ? null : new DurationSummary(_count, _min, _max, _sum / _count);
    }
}
