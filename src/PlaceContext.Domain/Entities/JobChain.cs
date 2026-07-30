using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: a staged pipeline of jobs — an ordered list of <see cref="ChainStage"/>s. A stage
/// with one job is an ordinary sequential step; a stage with more than one job is a parallel fan-out
/// group that all runs concurrently, and the stage right after it is the join, receiving every
/// branch's output. A chain whose stages are all size 1 is exactly the original linear chain — each
/// step's primary output (the reduce artifact when present, else the shard artifacts) becomes the
/// next stage's input payload. Running a chain produces one <see cref="JobRun"/> per job across every
/// stage; the chain is all-or-nothing per stage — it stops (and skips every later stage, including
/// the join) the moment any job in a stage fails.
/// The chain stores only job ids — steps are resolved at run time, so editing a job never touches the
/// chains that reference it (a deleted job simply fails the chain at that stage).
/// </summary>
public sealed class JobChain : AggregateRoot
{
    private readonly List<ChainStage> _stages;

    private JobChain(Guid id, Guid projectId, string name, string? description,
        List<ChainStage> stages, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        Description = description;
        _stages = stages;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }

    /// <summary>The pipeline's stages, in run order. A stage with more than one job id is a parallel
    /// fan-out group; the stage right after it is the join.</summary>
    public IReadOnlyList<ChainStage> Stages => _stages;

    /// <summary>
    /// Backward-compatible flattened view of every job id across every stage, in run order. For a
    /// linear chain (every stage size 1) this is exactly the original step list.
    /// </summary>
    public IReadOnlyList<Guid> StepJobIds => _stages.SelectMany(s => s.JobIds).ToList();

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static JobChain Create(Guid projectId, string name, string? description,
        IReadOnlyList<ChainStage> stages, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Chain name must not be empty.", nameof(name));
        return new JobChain(Guid.NewGuid(), projectId, name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            ValidateStages(stages), now, now);
    }

    /// <summary>Backward-compatible factory: one job id per stage — a purely sequential chain.</summary>
    public static JobChain Create(Guid projectId, string name, string? description,
        IReadOnlyList<Guid> stepJobIds, DateTimeOffset now)
        => Create(projectId, name, description, ToLinearStages(stepJobIds), now);

    public void Update(string name, string? description, IReadOnlyList<ChainStage> stages, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Chain name must not be empty.", nameof(name));
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        var validated = ValidateStages(stages);
        _stages.Clear();
        _stages.AddRange(validated);
        UpdatedAt = now;
    }

    /// <summary>Backward-compatible overload: replaces the chain with a purely sequential pipeline.</summary>
    public void Update(string name, string? description, IReadOnlyList<Guid> stepJobIds, DateTimeOffset now)
        => Update(name, description, ToLinearStages(stepJobIds), now);

    private static List<ChainStage> ToLinearStages(IReadOnlyList<Guid> stepJobIds)
    {
        if (stepJobIds is null)
            throw new ArgumentException("A chain needs at least one step (job).", nameof(stepJobIds));
        return stepJobIds.Select(ChainStage.Of).ToList();
    }

    private static List<ChainStage> ValidateStages(IReadOnlyList<ChainStage> stages)
    {
        if (stages is null || stages.Count == 0)
            throw new ArgumentException("A chain needs at least one stage.", nameof(stages));
        // Defensive copy — each ChainStage already validated its own job ids at construction, and a
        // stage may legitimately repeat a job (e.g. transform → verify → transform, or fan-out
        // branches sharing a job) — no dedup.
        return stages.Select(s => new ChainStage(s.JobIds, s.Gate, s.ElseBranch)).ToList();
    }

    public static JobChain Rehydrate(Guid id, Guid projectId, string name, string? description,
        IReadOnlyList<ChainStage> stages, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, projectId, name, description,
            stages.Select(s => new ChainStage(s.JobIds, s.Gate, s.ElseBranch)).ToList(), createdAt, updatedAt);
}
