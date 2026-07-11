using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: one execution of a <see cref="JobChain"/> as a staged pipeline. Persisted the
/// moment the chain starts and updated as each step begins and finishes, so the portal can show
/// the pipeline progressing live (stage chips: pending → running → outcome) and keep a run
/// history after the fact. Chain and job names are snapshotted — history stands alone even when
/// the chain or its jobs are later renamed or deleted.
/// </summary>
public sealed class ChainRun : AggregateRoot
{
    private readonly List<ChainStepRun> _steps;

    private ChainRun(Guid id, Guid chainId, Guid projectId, string chainName, ChainRunStatus status,
        List<ChainStepRun> steps, string? finalOutput, DateTimeOffset startedAt, DateTimeOffset? finishedAt)
    {
        Id = id;
        ChainId = chainId;
        ProjectId = projectId;
        ChainName = chainName;
        Status = status;
        _steps = steps;
        FinalOutput = finalOutput;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
    }

    public Guid Id { get; }
    public Guid ChainId { get; }
    public Guid ProjectId { get; }
    public string ChainName { get; }
    public ChainRunStatus Status { get; private set; }
    public IReadOnlyList<ChainStepRun> Steps => _steps;

    /// <summary>The last executed step's primary output — what the pipeline produced.</summary>
    public string? FinalOutput { get; private set; }

    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Starts a run: every step pending, the run itself Running. Callers may pre-allocate
    /// <paramref name="id"/> so the run is addressable before the handler returns.</summary>
    public static ChainRun Start(JobChain chain, IReadOnlyList<string> stepJobNames, DateTimeOffset now,
        Guid? id = null)
    {
        if (stepJobNames.Count != chain.StepJobIds.Count)
            throw new ArgumentException("One job name per chain step is required.", nameof(stepJobNames));
        if (id == Guid.Empty)
            throw new ArgumentException("A pre-allocated run id must not be empty.", nameof(id));
        var steps = chain.StepJobIds
            .Select((jobId, i) => new ChainStepRun(i, jobId, stepJobNames[i], null, ChainStepStatus.Pending, null, null))
            .ToList();
        return new ChainRun(id ?? Guid.NewGuid(), chain.Id, chain.ProjectId, chain.Name, ChainRunStatus.Running, steps, null, now, null);
    }

    /// <summary>Marks a step running. <paramref name="runId"/> is the step's pre-allocated job-run
    /// id, recorded up front so a live pipeline can link to the run while it executes.</summary>
    public void MarkStepRunning(int index, Guid? runId, DateTimeOffset now)
        => _steps[index] = _steps[index] with { Status = ChainStepStatus.Running, RunId = runId, StartedAt = now };

    public void MarkStepFinished(int index, Guid? runId, ChainStepStatus outcome, DateTimeOffset now)
    {
        if (outcome is ChainStepStatus.Pending or ChainStepStatus.Running)
            throw new ArgumentException("A finished step needs a terminal outcome.", nameof(outcome));
        _steps[index] = _steps[index] with { RunId = runId, Status = outcome, FinishedAt = now };
    }

    /// <summary>Finishes the run; steps that never started (after a failure) become Skipped.</summary>
    public void Complete(ChainRunStatus status, string? finalOutput, DateTimeOffset now)
    {
        if (status == ChainRunStatus.Running)
            throw new ArgumentException("A completed run needs a terminal status.", nameof(status));
        for (var i = 0; i < _steps.Count; i++)
            if (_steps[i].Status == ChainStepStatus.Pending)
                _steps[i] = _steps[i] with { Status = ChainStepStatus.Skipped };
        Status = status;
        FinalOutput = finalOutput;
        FinishedAt = now;
    }

    public static ChainRun Rehydrate(Guid id, Guid chainId, Guid projectId, string chainName,
        ChainRunStatus status, IReadOnlyList<ChainStepRun> steps, string? finalOutput,
        DateTimeOffset startedAt, DateTimeOffset? finishedAt)
        => new(id, chainId, projectId, chainName, status, steps.ToList(), finalOutput, startedAt, finishedAt);
}

/// <summary>One stage of a chain run: which job, the run it produced (once started), and where it
/// is in its lifecycle. JobName is snapshotted at start.</summary>
public sealed record ChainStepRun(
    int Index,
    Guid JobId,
    string JobName,
    Guid? RunId,
    ChainStepStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);
