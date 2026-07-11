using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Per-tenant watcher memory, owned by the background loop and threaded through every sweep.
/// In-memory by design — it mirrors the per-replica notification ledger it feeds, and after a
/// restart the first sweep rebuilds it from the persisted runs.
/// </summary>
public sealed class RunWatchState
{
    /// <summary>Terminal-status watermark for the reader query; default triggers the startup lookback.</summary>
    public DateTimeOffset Cursor { get; set; }

    /// <summary>Job runs already notified as Running.</summary>
    public HashSet<Guid> JobRuns { get; } = new();

    /// <summary>Chain runs already notified as Running → their last progress fingerprint.</summary>
    public Dictionary<Guid, string> Chains { get; } = new();

    /// <summary>Runs whose terminal status was already notified (the cursor overlap re-reads them).</summary>
    public Dictionary<Guid, DateTimeOffset> NotifiedTerminal { get; } = new();

    /// <summary>Job-run ids owned by a chain step → when last seen; suppressed from standalone notifications.</summary>
    public Dictionary<Guid, DateTimeOffset> ChainStepRuns { get; } = new();
}

/// <summary>
/// The run-status watcher: diffs the persisted job/chain runs against what was already notified
/// and pushes every transition (started, step advanced, finished, failed) to the notifier.
/// The database is the source of truth — <c>RunJobHandler</c> commits the terminal status before
/// its slow best-effort enrichment, so watching the rows surfaces outcomes minutes earlier than
/// the in-process caller can, and independently of which replica executes the run.
/// Job runs owned by a chain step are folded into the chain's own notification.
/// </summary>
public sealed class RunStatusWatchService
{
    /// <summary>How far back the first sweep looks for finished runs (repopulates the pane after a restart).</summary>
    public static readonly TimeSpan StartupLookback = TimeSpan.FromMinutes(10);

    /// <summary>Cursor slack so a row committing with a slightly older FinishedAt is never skipped.</summary>
    public static readonly TimeSpan CursorOverlap = TimeSpan.FromSeconds(30);

    /// <summary>How long dedupe/suppression memory is retained after a run leaves the read window.</summary>
    public static readonly TimeSpan MemoryRetention = TimeSpan.FromMinutes(10);

    private readonly IRunStatusReader _reader;
    private readonly IRunStatusNotifier _notifier;
    private readonly IClock _clock;

    public RunStatusWatchService(IRunStatusReader reader, IRunStatusNotifier notifier, IClock clock)
    {
        _reader = reader;
        _notifier = notifier;
        _clock = clock;
    }

    public static string JobRunKey(Guid runId) => $"job-run:{runId:N}";
    public static string ChainRunKey(Guid chainRunId) => $"chain-run:{chainRunId:N}";

    public async Task SweepAsync(RunWatchState state, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var since = state.Cursor == default ? now - StartupLookback : state.Cursor;

        // Chains first: their rows commit before their step runs do, so the suppression set is
        // always populated before the owned job runs are considered.
        var chains = await _reader.ListChainRunsAsync(since, ct);
        var jobRuns = await _reader.ListJobRunsAsync(since, ct);

        foreach (var chain in chains)
            foreach (var stepRunId in chain.StepRunIds)
                state.ChainStepRuns[stepRunId] = now;

        SweepChains(state, chains, now);
        SweepJobRuns(state, jobRuns, now);
        Evict(state, now);

        state.Cursor = now - CursorOverlap;
    }

    private void SweepChains(RunWatchState state, IReadOnlyList<ChainRunStatusRow> chains, DateTimeOffset now)
    {
        var seen = new HashSet<Guid>();
        foreach (var chain in chains)
        {
            var key = ChainRunKey(chain.RunId);
            var title = $"Run chain — {chain.ChainName}";
            var link = $"/project/{chain.ProjectId}/chains";

            if (chain.Status == ChainRunStatus.Running)
            {
                seen.Add(chain.RunId);
                var fingerprint = $"{chain.FinishedSteps}/{chain.TotalSteps}:{chain.CurrentStepName}";
                if (state.Chains.TryGetValue(chain.RunId, out var prev) && prev == fingerprint) continue;
                state.Chains[chain.RunId] = fingerprint;
                _notifier.Sync(new RunStatusUpdate(key, chain.ProjectId, title, RunOutcome.Running,
                    ChainProgress(chain), link, chain.StartedAt, null));
            }
            else
            {
                state.Chains.Remove(chain.RunId);
                if (!state.NotifiedTerminal.TryAdd(chain.RunId, chain.FinishedAt ?? now)) continue;
                _notifier.Sync(new RunStatusUpdate(key, chain.ProjectId, title, MapChain(chain.Status),
                    $"chain finished — {chain.Status} ({chain.FinishedSteps} step(s) ran)",
                    link, chain.StartedAt, chain.FinishedAt));
            }
        }

        // Rows that vanished without a terminal status (e.g. a deleted project) — forget them.
        foreach (var gone in state.Chains.Keys.Where(id => !seen.Contains(id)).ToList())
            state.Chains.Remove(gone);
    }

    private void SweepJobRuns(RunWatchState state, IReadOnlyList<JobRunStatusRow> jobRuns, DateTimeOffset now)
    {
        var seen = new HashSet<Guid>();
        foreach (var run in jobRuns)
        {
            if (state.ChainStepRuns.ContainsKey(run.RunId)) continue; // the chain's notification covers it

            var key = JobRunKey(run.RunId);
            var title = $"Run job — {run.JobName}";
            var link = $"/project/{run.ProjectId}/jobs";

            if (run.Status is JobRunStatus.Running or JobRunStatus.Queued)
            {
                seen.Add(run.RunId);
                if (!state.JobRuns.Add(run.RunId)) continue;
                _notifier.Sync(new RunStatusUpdate(key, run.ProjectId, title, RunOutcome.Running,
                    null, link, run.StartedAt, null));
            }
            else
            {
                state.JobRuns.Remove(run.RunId);
                if (!state.NotifiedTerminal.TryAdd(run.RunId, run.FinishedAt ?? now)) continue;
                _notifier.Sync(new RunStatusUpdate(key, run.ProjectId, title, Map(run.Status),
                    $"run finished — {run.Status}", link, run.StartedAt, run.FinishedAt));
            }
        }

        foreach (var gone in state.JobRuns.Where(id => !seen.Contains(id)).ToList())
            state.JobRuns.Remove(gone);
    }

    private static void Evict(RunWatchState state, DateTimeOffset now)
    {
        var floor = now - MemoryRetention;
        foreach (var id in state.NotifiedTerminal.Where(kv => kv.Value < floor).Select(kv => kv.Key).ToList())
            state.NotifiedTerminal.Remove(id);
        foreach (var id in state.ChainStepRuns.Where(kv => kv.Value < floor).Select(kv => kv.Key).ToList())
            state.ChainStepRuns.Remove(id);
    }

    private static string ChainProgress(ChainRunStatusRow chain)
        => chain.CurrentStepName is { Length: > 0 } name
            ? $"step {Math.Min(chain.FinishedSteps + 1, chain.TotalSteps)}/{chain.TotalSteps} — {name}"
            : $"{chain.FinishedSteps}/{chain.TotalSteps} step(s) done";

    private static RunOutcome Map(JobRunStatus status) => status switch
    {
        JobRunStatus.Succeeded => RunOutcome.Succeeded,
        JobRunStatus.Partial => RunOutcome.Partial,
        _ => RunOutcome.Failed,
    };

    private static RunOutcome MapChain(ChainRunStatus status) => status switch
    {
        ChainRunStatus.Succeeded => RunOutcome.Succeeded,
        ChainRunStatus.Partial => RunOutcome.Partial,
        _ => RunOutcome.Failed,
    };
}
