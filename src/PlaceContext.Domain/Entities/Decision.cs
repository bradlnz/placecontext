using PlaceContext.Domain.Common;
using PlaceContext.Domain.Events;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: a lightweight Architecture Decision Record. Captures the question faced, the
/// choice made, and the rationale — and which prior decisions it supersedes.
/// </summary>
public sealed class Decision : AggregateRoot
{
    private readonly List<DecisionId> _supersedes;

    private Decision(
        DecisionId id,
        ProjectId projectId,
        string question,
        string choice,
        Rationale rationale,
        IEnumerable<DecisionId> supersedes,
        DateTimeOffset decidedAt)
    {
        Id = id;
        ProjectId = projectId;
        Question = question;
        Choice = choice;
        Rationale = rationale;
        _supersedes = supersedes.ToList();
        DecidedAt = decidedAt;
    }

    public DecisionId Id { get; }
    public ProjectId ProjectId { get; }
    public string Question { get; }
    public string Choice { get; }
    public Rationale Rationale { get; }
    public IReadOnlyList<DecisionId> Supersedes => _supersedes;
    public DateTimeOffset DecidedAt { get; }

    public static Decision Record(
        ProjectId projectId,
        string question,
        string choice,
        Rationale rationale,
        DateTimeOffset decidedAt,
        IEnumerable<DecisionId>? supersedes = null)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Decision question must not be empty.", nameof(question));
        if (string.IsNullOrWhiteSpace(choice))
            throw new ArgumentException("Decision choice must not be empty.", nameof(choice));
        ArgumentNullException.ThrowIfNull(rationale);

        var decision = new Decision(
            DecisionId.New(), projectId, question.Trim(), choice.Trim(),
            rationale, supersedes ?? Array.Empty<DecisionId>(), decidedAt);
        decision.Raise(new DecisionRecorded(projectId, decision.Id, decidedAt));
        return decision;
    }

    public static Decision Rehydrate(
        DecisionId id,
        ProjectId projectId,
        string question,
        string choice,
        Rationale rationale,
        IEnumerable<DecisionId> supersedes,
        DateTimeOffset decidedAt)
        => new(id, projectId, question, choice, rationale, supersedes, decidedAt);
}
