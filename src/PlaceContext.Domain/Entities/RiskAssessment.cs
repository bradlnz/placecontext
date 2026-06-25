using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: an immutable snapshot of a project's risk at a point in time — the two banded
/// scores plus the individual signals that produced them. Stored per recompute to drive trends.
/// </summary>
public sealed class RiskAssessment
{
    private readonly List<RiskSignal> _signals;

    private RiskAssessment(
        AssessmentId id,
        ProjectId projectId,
        RiskScore technical,
        RiskScore process,
        IEnumerable<RiskSignal> signals,
        DateTimeOffset computedAt)
    {
        Id = id;
        ProjectId = projectId;
        Technical = technical;
        Process = process;
        _signals = signals.ToList();
        ComputedAt = computedAt;
    }

    public AssessmentId Id { get; }
    public ProjectId ProjectId { get; }
    public RiskScore Technical { get; }
    public RiskScore Process { get; }
    public IReadOnlyList<RiskSignal> Signals => _signals;
    public DateTimeOffset ComputedAt { get; }

    public IEnumerable<RiskSignal> TechnicalSignals => _signals.Where(s => s.Kind == RiskKind.Technical);
    public IEnumerable<RiskSignal> ProcessSignals => _signals.Where(s => s.Kind == RiskKind.Process);

    public static RiskAssessment Create(
        ProjectId projectId,
        RiskScore technical,
        RiskScore process,
        IEnumerable<RiskSignal> signals,
        DateTimeOffset computedAt)
    {
        ArgumentNullException.ThrowIfNull(technical);
        ArgumentNullException.ThrowIfNull(process);

        return new RiskAssessment(AssessmentId.New(), projectId, technical, process, signals, computedAt);
    }

    public static RiskAssessment Rehydrate(
        AssessmentId id,
        ProjectId projectId,
        RiskScore technical,
        RiskScore process,
        IEnumerable<RiskSignal> signals,
        DateTimeOffset computedAt)
        => new(id, projectId, technical, process, signals, computedAt);
}
