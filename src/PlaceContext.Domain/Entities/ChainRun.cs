using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: one execution of a <see cref="JobChain"/> as a staged pipeline. Persisted the
/// moment the chain starts and updated as each step begins and finishes, so the portal can show
/// the pipeline progressing live (stage chips: pending → running → outcome) and keep a run
/// history after the fact. Chain and job names are snapshotted — history stands alone even when
/// the chain or its jobs are later renamed or deleted.
///
/// A stage with more than one step is a parallel fan-out group: its steps may be Running/finish
/// concurrently (the caller — <c>RunJobChainHandler</c> — dispatches them together and serializes
/// writes back onto this aggregate). All-or-nothing applies per stage: the caller only advances past
/// a stage once every one of its steps reached a non-Failed terminal state; any Failed step in a
/// stage halts the whole chain, and steps in every later stage (including the join) are left Pending
/// so <see cref="Complete"/> turns them into Skipped.
/// </summary>
public sealed class ChainRun : AggregateRoot
{
    private readonly List<ChainStepRun> _steps;

    private ChainRun(Guid id, Guid chainId, Guid projectId, string chainName, ChainRunStatus status,
        List<ChainStepRun> steps, string? finalOutput, DateTimeOffset startedAt, DateTimeOffset? finishedAt,
        DateTimeOffset? resumeAt = null, int? resumeStageIndex = null, Guid? crmClientId = null,
        string? continuationOverridesJson = null)
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
        ResumeAt = resumeAt;
        ResumeStageIndex = resumeStageIndex;
        CrmClientId = crmClientId;
        ContinuationOverridesJson = continuationOverridesJson;
    }

    public Guid Id { get; }
    public Guid ChainId { get; }
    public Guid ProjectId { get; }
    public string ChainName { get; }
    public ChainRunStatus Status { get; private set; }
    public IReadOnlyList<ChainStepRun> Steps => _steps;

    /// <summary>Steps grouped by stage, in run order — a group with more than one entry is a
    /// parallel fan-out stage (or the join that follows one, when it has a single entry).</summary>
    public IReadOnlyList<IReadOnlyList<ChainStepRun>> StepsByStage => _steps
        .GroupBy(s => s.StageIndex)
        .OrderBy(g => g.Key)
        .Select(g => (IReadOnlyList<ChainStepRun>)g.OrderBy(s => s.BranchIndex).ToList())
        .ToList();

    /// <summary>How many stages this run has.</summary>
    public int StageCount => _steps.Count == 0 ? 0 : _steps.Max(s => s.StageIndex) + 1;

    /// <summary>The last executed stage's primary output — what the pipeline produced. For a
    /// fan-out stage this is the join's output (or, if the chain failed inside the fan-out itself,
    /// the last branch's output the caller chose to carry forward).</summary>
    public string? FinalOutput { get; private set; }

    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public DateTimeOffset? ResumeAt { get; private set; }
    public int? ResumeStageIndex { get; private set; }
    public Guid? CrmClientId { get; private set; }
    public string? ContinuationOverridesJson { get; private set; }

    /// <summary>Starts a run: every step pending, the run itself Running. Callers may pre-allocate
    /// <paramref name="id"/> so the run is addressable before the handler returns. One job name is
    /// required per job across every stage, in stage order then branch order within a stage (i.e.
    /// the same flattening as <see cref="JobChain.StepJobIds"/>).</summary>
    public static ChainRun Start(JobChain chain, IReadOnlyList<string> stepJobNames, DateTimeOffset now,
        Guid? id = null, Guid? crmClientId = null)
    {
        if (stepJobNames.Count != chain.ExecutionStepCount)
            throw new ArgumentException("One display name per chain step is required.", nameof(stepJobNames));
        if (id == Guid.Empty)
            throw new ArgumentException("A pre-allocated run id must not be empty.", nameof(id));

        var steps = new List<ChainStepRun>(stepJobNames.Count);
        var flatIndex = 0;
        for (var stageIndex = 0; stageIndex < chain.Stages.Count; stageIndex++)
        {
            var stage = chain.Stages[stageIndex];
            if (stage.Action is { } action)
            {
                steps.Add(new ChainStepRun(flatIndex, stageIndex, 0, Guid.Empty,
                    stepJobNames[flatIndex], null, ChainStepStatus.Pending, null, null,
                    action.Type));
                flatIndex++;
                continue;
            }
            for (var branchIndex = 0; branchIndex < stage.JobIds.Count; branchIndex++)
            {
                steps.Add(new ChainStepRun(flatIndex, stageIndex, branchIndex, stage.JobIds[branchIndex],
                    stepJobNames[flatIndex], null, ChainStepStatus.Pending, null, null));
                flatIndex++;
            }
        }
        return new ChainRun(id ?? Guid.NewGuid(), chain.Id, chain.ProjectId, chain.Name, ChainRunStatus.Running,
            steps, null, now, null, crmClientId: crmClientId);
    }

    public void Pause(int resumeStageIndex, string? payload, DateTimeOffset resumeAt,
        string? continuationOverridesJson = null)
    {
        if (resumeStageIndex < 0 || resumeStageIndex >= StageCount)
            throw new ArgumentOutOfRangeException(nameof(resumeStageIndex));
        Status = ChainRunStatus.Waiting;
        FinalOutput = payload;
        ResumeAt = resumeAt;
        ResumeStageIndex = resumeStageIndex;
        FinishedAt = null;
        ContinuationOverridesJson = continuationOverridesJson;
    }

    public void Resume()
    {
        if (Status != ChainRunStatus.Waiting)
            throw new InvalidOperationException("Only a waiting chain run can be resumed.");
        Status = ChainRunStatus.Running;
    }

    /// <summary>Marks a step running. <paramref name="runId"/> is the step's pre-allocated job-run
    /// id, recorded up front so a live pipeline can link to the run while it executes. Steps within
    /// the same stage may be marked running independently of one another (a fan-out group runs
    /// concurrently) — callers must serialize their own writes onto this aggregate.</summary>
    public void MarkStepRunning(int index, Guid? runId, DateTimeOffset now)
        => _steps[index] = _steps[index] with { Status = ChainStepStatus.Running, RunId = runId, StartedAt = now };

    public void MarkStepFinished(int index, Guid? runId, ChainStepStatus outcome, DateTimeOffset now,
        string? provider = null, string? externalId = null, string? error = null)
    {
        if (outcome is ChainStepStatus.Pending or ChainStepStatus.Running)
            throw new ArgumentException("A finished step needs a terminal outcome.", nameof(outcome));
        _steps[index] = _steps[index] with
        {
            RunId = runId,
            Status = outcome,
            FinishedAt = now,
            Provider = provider,
            ExternalId = externalId,
            Error = error,
        };
    }

    /// <summary>True once every step in the given stage has reached a terminal (non-pending,
    /// non-running) status.</summary>
    public bool IsStageSettled(int stageIndex)
        => _steps.Where(s => s.StageIndex == stageIndex)
            .All(s => s.Status is not (ChainStepStatus.Pending or ChainStepStatus.Running));

    /// <summary>True when any step in the given stage ended Failed — the all-or-nothing trigger that
    /// halts the chain and skips every later stage, including the join.</summary>
    public bool StageFailed(int stageIndex)
        => _steps.Where(s => s.StageIndex == stageIndex).Any(s => s.Status == ChainStepStatus.Failed);

    /// <summary>True when any step in the given stage ended Partial — downgrades the chain's final
    /// status without halting it.</summary>
    public bool StagePartial(int stageIndex)
        => _steps.Where(s => s.StageIndex == stageIndex).Any(s => s.Status == ChainStepStatus.Partial);

    /// <summary>Finishes the run; steps that never started (after a failure) become Skipped;
    /// when the run was Cancelled, pending steps become Cancelled instead.</summary>
    public void Complete(ChainRunStatus status, string? finalOutput, DateTimeOffset now)
    {
        if (status is ChainRunStatus.Running or ChainRunStatus.Waiting)
            throw new ArgumentException("A completed run needs a terminal status.", nameof(status));
        var cancelled = status == ChainRunStatus.Cancelled;
        for (var i = 0; i < _steps.Count; i++)
            if (_steps[i].Status == ChainStepStatus.Pending)
                _steps[i] = _steps[i] with { Status = cancelled ? ChainStepStatus.Cancelled : ChainStepStatus.Skipped };
        Status = status;
        FinalOutput = finalOutput;
        FinishedAt = now;
        ResumeAt = null;
        ResumeStageIndex = null;
        ContinuationOverridesJson = null;
    }

    public static ChainRun Rehydrate(Guid id, Guid chainId, Guid projectId, string chainName,
        ChainRunStatus status, IReadOnlyList<ChainStepRun> steps, string? finalOutput,
        DateTimeOffset startedAt, DateTimeOffset? finishedAt, DateTimeOffset? resumeAt = null,
        int? resumeStageIndex = null, Guid? crmClientId = null, string? continuationOverridesJson = null)
        => new(id, chainId, projectId, chainName, status, steps.ToList(), finalOutput, startedAt,
            finishedAt, resumeAt, resumeStageIndex, crmClientId, continuationOverridesJson);
}

/// <summary>One step of a chain run: which job, which stage/branch position it occupies, the run it
/// produced (once started), and where it is in its lifecycle. JobName is snapshotted at start.
/// <paramref name="Index"/> is the step's flat position across every stage (0-based, stage order
/// then branch order) — the same addressing scheme <see cref="ChainRun.MarkStepRunning"/> and
/// <see cref="ChainRun.MarkStepFinished"/> use. <paramref name="StageIndex"/> is which stage the
/// step belongs to; <paramref name="BranchIndex"/> is its 0-based position within that stage (always
/// 0 for a size-1 stage — i.e. every step of a linear chain).</summary>
public sealed record ChainStepRun(
    int Index,
    int StageIndex,
    int BranchIndex,
    Guid JobId,
    string JobName,
    Guid? RunId,
    ChainStepStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ActionType = null,
    string? Provider = null,
    string? ExternalId = null,
    string? Error = null);
