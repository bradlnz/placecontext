using PlaceContext.Domain.Common;
using PlaceContext.Domain.Events;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: the append-only ledger of every change made to a project. Sequence numbers are
/// monotonic and contiguous from 1. The ledger is the only place a <see cref="ActivityRecord"/> is
/// created, guaranteeing the append-only invariant. One ledger per project.
/// </summary>
public sealed class ActivityLog : AggregateRoot
{
    private readonly List<ActivityRecord> _records;

    private ActivityLog(ProjectId projectId, IEnumerable<ActivityRecord> records)
    {
        ProjectId = projectId;
        _records = records.OrderBy(r => r.Sequence).ToList();
    }

    public ProjectId ProjectId { get; }
    public IReadOnlyList<ActivityRecord> Records => _records;
    public int Count => _records.Count;

    /// <summary>An empty ledger for a project.</summary>
    public static ActivityLog Start(ProjectId projectId)
        => new(projectId, Array.Empty<ActivityRecord>());

    /// <summary>Rebuilds a ledger from persisted records. Used only by the Infrastructure repository.</summary>
    public static ActivityLog Rehydrate(ProjectId projectId, IEnumerable<ActivityRecord> records)
        => new(projectId, records);

    /// <summary>
    /// Appends a new change. The next sequence number is assigned here, enforcing monotonicity and
    /// the append-only invariant. Raises <see cref="ActivityRecorded"/>.
    /// </summary>
    public ActivityRecord Append(
        string summary,
        Author author,
        Rationale rationale,
        TestDelta testDelta,
        ActivityVerification verification,
        IEnumerable<string> touchedFiles,
        IEnumerable<GraphNodeId> touchedNodes,
        DateTimeOffset recordedAt)
    {
        var record = ActivityRecord.Create(
            sequence: _records.Count + 1,
            summary, author, rationale, testDelta, verification,
            touchedFiles, touchedNodes, recordedAt);

        _records.Add(record);
        Raise(new ActivityRecorded(ProjectId, record.Id, author, recordedAt));
        return record;
    }

    /// <summary>The most recent <paramref name="n"/> records, newest last.</summary>
    public IReadOnlyList<ActivityRecord> RecentWindow(int n)
    {
        if (n <= 0) return Array.Empty<ActivityRecord>();
        return _records.Skip(Math.Max(0, _records.Count - n)).ToList();
    }

    /// <summary>
    /// True when any of <paramref name="nodes"/> was already touched by one of the previous
    /// <paramref name="window"/> records (excluding <paramref name="excludingSequence"/>) — i.e.
    /// the same hotspot is being churned.
    /// </summary>
    public bool TouchesWithin(IEnumerable<GraphNodeId> nodes, int window, int excludingSequence)
    {
        if (window <= 0) return false;
        var target = nodes.ToHashSet();
        if (target.Count == 0) return false;

        return _records
            .Where(r => r.Sequence != excludingSequence)
            .Skip(Math.Max(0, _records.Count - window - 1))
            .Any(r => r.TouchedNodes.Any(target.Contains));
    }
}
