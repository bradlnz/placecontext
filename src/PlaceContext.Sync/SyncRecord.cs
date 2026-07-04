namespace PlaceContext.Sync;

/// <summary>
/// One replicated unit of history — an activity-log record as it travels between nodes. Identified
/// by (<see cref="Origin"/>, <see cref="Sequence"/>): unique and immutable, so applying it twice is
/// a no-op and merge order never matters. <see cref="Payload"/> is opaque to the protocol. Value Object.
/// </summary>
public sealed record SyncRecord
{
    public NodeId Origin { get; }

    /// <summary>Strictly-increasing sequence at the origin node (1-based).</summary>
    public long Sequence { get; }

    public Guid ProjectId { get; }

    /// <summary>Application-defined record kind (e.g. "activity", "decision", "embedding").</summary>
    public string Kind { get; }

    /// <summary>Content digest of <see cref="Payload"/> for integrity/dedup; opaque here.</summary>
    public string Digest { get; }

    public byte[] Payload { get; }

    private SyncRecord(NodeId origin, long sequence, Guid projectId, string kind, string digest, byte[] payload)
    {
        Origin = origin;
        Sequence = sequence;
        ProjectId = projectId;
        Kind = kind;
        Digest = digest;
        Payload = payload;
    }

    public static SyncRecord Create(
        NodeId origin, long sequence, Guid projectId, string kind, string digest, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be positive.");
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Kind must not be empty.", nameof(kind));
        return new SyncRecord(origin, sequence, projectId, kind, digest ?? "", payload ?? Array.Empty<byte>());
    }

    /// <summary>Stable identity key for dedup: "origin/sequence".</summary>
    public string Key => $"{Origin.Value}/{Sequence}";
}
