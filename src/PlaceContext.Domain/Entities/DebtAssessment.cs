using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: an immutable snapshot of a project's debt at a point in time — the two banded
/// scores plus the individual signals that produced them. Stored per recompute to drive trends.
/// </summary>
public sealed class DebtAssessment
{
    private readonly List<DebtSignal> _signals;

    private DebtAssessment(
        AssessmentId id,
        ProjectId projectId,
        DebtScore technical,
        DebtScore agentic,
        IEnumerable<DebtSignal> signals,
        DateTimeOffset computedAt)
    {
        Id = id;
        ProjectId = projectId;
        Technical = technical;
        Agentic = agentic;
        _signals = signals.ToList();
        ComputedAt = computedAt;
    }

    public AssessmentId Id { get; }
    public ProjectId ProjectId { get; }
    public DebtScore Technical { get; }
    public DebtScore Agentic { get; }
    public IReadOnlyList<DebtSignal> Signals => _signals;
    public DateTimeOffset ComputedAt { get; }

    public IEnumerable<DebtSignal> TechnicalSignals => _signals.Where(s => s.Kind == DebtKind.Technical);
    public IEnumerable<DebtSignal> AgenticSignals => _signals.Where(s => s.Kind == DebtKind.Agentic);

    public static DebtAssessment Create(
        ProjectId projectId,
        DebtScore technical,
        DebtScore agentic,
        IEnumerable<DebtSignal> signals,
        DateTimeOffset computedAt)
    {
        ArgumentNullException.ThrowIfNull(technical);
        ArgumentNullException.ThrowIfNull(agentic);

        return new DebtAssessment(AssessmentId.New(), projectId, technical, agentic, signals, computedAt);
    }

    public static DebtAssessment Rehydrate(
        AssessmentId id,
        ProjectId projectId,
        DebtScore technical,
        DebtScore agentic,
        IEnumerable<DebtSignal> signals,
        DateTimeOffset computedAt)
        => new(id, projectId, technical, agentic, signals, computedAt);
}
