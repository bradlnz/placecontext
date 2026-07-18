using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// An entity within the <see cref="ActivityLog"/> aggregate. Immutable except for the one-time
/// attachment of the git commit it produced. The ledger is its only mutator; outside code never
/// constructs or edits a record directly.
/// </summary>
public sealed class ActivityRecord
{
    private readonly List<string> _touchedFiles;
    private readonly List<GraphNodeId> _touchedNodes;

    internal ActivityRecord(
        ActivityRecordId id,
        int sequence,
        string summary,
        Author author,
        Rationale rationale,
        TestDelta testDelta,
        ActivityVerification verification,
        IEnumerable<string> touchedFiles,
        IEnumerable<GraphNodeId> touchedNodes,
        CommitSha? commit,
        DateTimeOffset recordedAt)
    {
        Id = id;
        Sequence = sequence;
        Summary = summary;
        Author = author;
        Rationale = rationale;
        TestDelta = testDelta;
        Verification = verification;
        _touchedFiles = touchedFiles.ToList();
        _touchedNodes = touchedNodes.ToList();
        Commit = commit;
        RecordedAt = recordedAt;
    }

    public ActivityRecordId Id { get; }
    public int Sequence { get; }
    /// <summary>What changed (the commit message / headline). The "why" is <see cref="Rationale"/>.</summary>
    public string Summary { get; }
    public Author Author { get; }
    public Rationale Rationale { get; }
    public TestDelta TestDelta { get; }
    public ActivityVerification Verification { get; }
    public IReadOnlyList<string> TouchedFiles => _touchedFiles;
    public IReadOnlyList<GraphNodeId> TouchedNodes => _touchedNodes;
    public CommitSha? Commit { get; private set; }
    public DateTimeOffset RecordedAt { get; }

    public bool IsAgentAuthored => Author.IsAgent;
    public bool IsCommitted => Commit is not null;

    /// <summary>Factory used by the ledger when appending a new change.</summary>
    internal static ActivityRecord Create(
        int sequence,
        string summary,
        Author author,
        Rationale rationale,
        TestDelta testDelta,
        ActivityVerification verification,
        IEnumerable<string> touchedFiles,
        IEnumerable<GraphNodeId> touchedNodes,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(rationale);

        return new ActivityRecord(
            ActivityRecordId.New(), sequence, summary ?? string.Empty, author, rationale, testDelta,
            verification, touchedFiles, touchedNodes, commit: null, recordedAt);
    }

    /// <summary>Rebuilds a record from persisted state (including its commit). Used only by the Infrastructure repository.</summary>
    public static ActivityRecord Rehydrate(
        ActivityRecordId id,
        int sequence,
        string summary,
        Author author,
        Rationale rationale,
        TestDelta testDelta,
        ActivityVerification verification,
        IEnumerable<string> touchedFiles,
        IEnumerable<GraphNodeId> touchedNodes,
        CommitSha? commit,
        DateTimeOffset recordedAt)
        => new(id, sequence, summary ?? string.Empty, author, rationale, testDelta, verification,
               touchedFiles, touchedNodes, commit, recordedAt);

    /// <summary>Attaches the git commit this change produced. Allowed once; the commit is then immutable.</summary>
    public void AttachCommit(CommitSha commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (Commit is not null)
            throw new InvalidOperationException("This change already has a commit attached.");

        Commit = commit;
    }
}
