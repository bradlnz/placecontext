using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
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
    private const int MaxChainRuns = 200;

    private readonly object _runsGate = new();
    private readonly LinkedList<JobRunTelemetry> _runs = new(); // newest at the front

    private readonly object _chainRunsGate = new();
    private readonly LinkedList<ChainRunTelemetry> _chainRuns = new(); // newest at the front

    private readonly ConcurrentDictionary<Guid, ConcurrentBag<ShardTelemetry>> _pendingShards = new();

    private long _runsStarted;
    private readonly ConcurrentDictionary<string, long> _runsCompletedByStatus = new();
    private readonly ConcurrentDictionary<string, long> _shardsCompletedByOutcome = new();
    private readonly DurationAccumulator _runDuration = new();
    private readonly DurationAccumulator _shardDuration = new();

    private long _chainsStarted;
    private readonly ConcurrentDictionary<string, long> _chainsCompletedByStatus = new();
    private readonly DurationAccumulator _chainDuration = new();

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
            case "job.chain": CaptureChain(activity); break;
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
            activity.Duration.TotalMilliseconds, shards,
            activity.TraceId.ToHexString(),
            activity.SpanId.ToHexString());

        lock (_runsGate)
        {
            _runs.AddFirst(telemetry);
            while (_runs.Count > MaxRuns) _runs.RemoveLast();
        }
    }

    /// <summary>
    /// Captures a finished <c>job.chain</c> activity. Its step breakdown (stage/branch position, run
    /// id, outcome) isn't correlated from the child <c>job.run</c> activities' own tags — that data
    /// only <c>RunJobChainHandler</c> has, and reaching into <c>RunJobHandler</c> to tag it there is
    /// out of scope for this change — so the chain publishes its own step summary as a single
    /// <c>chain.steps.json</c> tag when it finishes, and this just parses it back.
    /// </summary>
    private void CaptureChain(Activity activity)
    {
        var chainRunId = TagGuid(activity, "chain.run.id") ?? Guid.Empty;
        var chainId = TagGuid(activity, "chain.id") ?? Guid.Empty;
        var chainName = TagString(activity, "chain.name");
        var projectId = TagGuid(activity, "project.id");
        var status = TagString(activity, "status");

        IReadOnlyList<ChainRunStepTelemetry> steps = Array.Empty<ChainRunStepTelemetry>();
        if (TagString(activity, "chain.steps.json") is { Length: > 0 } stepsJson)
        {
            try { steps = ParseSteps(stepsJson); }
            catch (JsonException) { /* malformed tag — keep the chain telemetry without step detail */ }
        }

        var telemetry = new ChainRunTelemetry(
            chainRunId, chainId, chainName, projectId, status,
            new DateTimeOffset(activity.StartTimeUtc, TimeSpan.Zero),
            activity.Duration.TotalMilliseconds, steps);

        lock (_chainRunsGate)
        {
            _chainRuns.AddFirst(telemetry);
            while (_chainRuns.Count > MaxChainRuns) _chainRuns.RemoveLast();
        }
    }

    private static IReadOnlyList<ChainRunStepTelemetry> ParseSteps(string stepsJson)
    {
        using var doc = JsonDocument.Parse(stepsJson);
        var steps = new List<ChainRunStepTelemetry>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            steps.Add(new ChainRunStepTelemetry(
                StageIndex: el.TryGetProperty("stageIndex", out var st) ? st.GetInt32() : 0,
                BranchIndex: el.TryGetProperty("branchIndex", out var br) ? br.GetInt32() : 0,
                JobId: el.TryGetProperty("jobId", out var ji) && ji.GetGuid() is var g ? g : Guid.Empty,
                JobName: el.TryGetProperty("jobName", out var jn) ? jn.GetString() : null,
                RunId: el.TryGetProperty("runId", out var ri) && ri.ValueKind != JsonValueKind.Null ? ri.GetGuid() : null,
                Status: el.TryGetProperty("status", out var s) ? s.GetString() : null,
                DurationMs: el.TryGetProperty("durationMs", out var d) && d.ValueKind != JsonValueKind.Null ? d.GetDouble() : null));
        }
        return steps;
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
            case "placecontext.jobs.chains.started":
                Interlocked.Add(ref _chainsStarted, measurement);
                break;
            case "placecontext.jobs.chains.completed":
                Increment(_chainsCompletedByStatus, TagValue(tags, "status"), measurement);
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
            case "placecontext.jobs.chain.duration": _chainDuration.Record(measurement); break;
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
        _shardDuration.ToSummary(),
        Interlocked.Read(ref _chainsStarted),
        new Dictionary<string, long>(_chainsCompletedByStatus),
        _chainDuration.ToSummary());

    public IReadOnlyList<JobRunTelemetry> RecentRuns(int take = 50)
    {
        lock (_runsGate) return _runs.Take(take).ToList();
    }

    public IReadOnlyList<JobRunTelemetry> RunsForJob(Guid jobId, int take = 20)
    {
        lock (_runsGate) return _runs.Where(r => r.JobId == jobId).Take(take).ToList();
    }

    public IReadOnlyList<ChainRunTelemetry> RecentChainRuns(int take = 50)
    {
        lock (_chainRunsGate) return _chainRuns.Take(take).ToList();
    }

    public IReadOnlyList<TraceSpanNode> TraceForRun(Guid runId)
    {
        JobRunTelemetry? run;
        lock (_runsGate) run = _runs.FirstOrDefault(r => r.RunId == runId);
        if (run is null) return Array.Empty<TraceSpanNode>();

        var shardChildren = run.Shards.OrderBy(s => s.Index).Select(s =>
        {
            var tags = new Dictionary<string, string>
            {
                ["shard.index"] = s.Index.ToString(),
            };
            if (s.Outcome is not null) tags["shard.outcome"] = s.Outcome;
            if (s.ExitCode is not null) tags["shard.exit_code"] = s.ExitCode.Value.ToString();
            return new TraceSpanNode(
                Name: $"job.shard[{s.Index}]",
                TraceId: run.TraceId,
                SpanId: null,
                ParentSpanId: run.SpanId,
                StartedAt: run.StartedAt,
                DurationMs: s.DurationMs ?? 0,
                Tags: tags,
                Children: Array.Empty<TraceSpanNode>());
        }).ToList();

        var rootTags = new Dictionary<string, string>
        {
            ["run.id"] = run.RunId.ToString(),
            ["job.id"] = run.JobId.ToString(),
        };
        if (run.JobName is not null) rootTags["job.name"] = run.JobName;
        if (run.Status is not null) rootTags["run.status"] = run.Status;
        if (run.Replay) rootTags["job.replay"] = "true";

        return new[]
        {
            new TraceSpanNode(
                Name: "job.run",
                TraceId: run.TraceId,
                SpanId: run.SpanId,
                ParentSpanId: null,
                StartedAt: run.StartedAt,
                DurationMs: run.DurationMs ?? 0,
                Tags: rootTags,
                Children: shardChildren),
        };
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }
}
