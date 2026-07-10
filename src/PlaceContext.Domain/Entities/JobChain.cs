using PlaceContext.Domain.Common;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: an ordered pipeline of jobs — each step's primary output (the reduce artifact when
/// present, else the shard artifacts) becomes the next step's input payload. Running a chain produces
/// one <see cref="JobRun"/> per step; the chain stops at the first failed step.
/// The chain stores only job ids — steps are resolved at run time, so editing a job never touches the
/// chains that reference it (a deleted job simply fails the chain at that step).
/// </summary>
public sealed class JobChain : AggregateRoot
{
    private readonly List<Guid> _stepJobIds;

    private JobChain(Guid id, Guid projectId, string name, string? description,
        List<Guid> stepJobIds, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        Description = description;
        _stepJobIds = stepJobIds;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }

    /// <summary>The jobs to run, in order.</summary>
    public IReadOnlyList<Guid> StepJobIds => _stepJobIds;

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static JobChain Create(Guid projectId, string name, string? description,
        IReadOnlyList<Guid> stepJobIds, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Chain name must not be empty.", nameof(name));
        return new JobChain(Guid.NewGuid(), projectId, name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            ValidateSteps(stepJobIds), now, now);
    }

    public void Update(string name, string? description, IReadOnlyList<Guid> stepJobIds, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Chain name must not be empty.", nameof(name));
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        _stepJobIds.Clear();
        _stepJobIds.AddRange(ValidateSteps(stepJobIds));
        UpdatedAt = now;
    }

    private static List<Guid> ValidateSteps(IReadOnlyList<Guid> stepJobIds)
    {
        if (stepJobIds is null || stepJobIds.Count == 0)
            throw new ArgumentException("A chain needs at least one step (job).", nameof(stepJobIds));
        if (stepJobIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("Chain steps must be real job ids.", nameof(stepJobIds));
        // The same job may appear more than once (e.g. transform → verify → transform) — no dedup.
        return stepJobIds.ToList();
    }

    public static JobChain Rehydrate(Guid id, Guid projectId, string name, string? description,
        IReadOnlyList<Guid> stepJobIds, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, projectId, name, description, stepJobIds.ToList(), createdAt, updatedAt);
}
