namespace PlaceContext.Sync;

/// <summary>
/// The record store a <see cref="SyncSession"/> reconciles against — the seam between the
/// transport-agnostic protocol and the node's actual storage (Postgres in the Host, in-memory in
/// tests). Implementations must make <see cref="Apply"/> idempotent by the record's
/// <see cref="SyncRecord.Key"/>.
/// </summary>
public interface ISyncStore
{
    /// <summary>The node's current knowledge frontier (highest sequence held per origin).</summary>
    VectorClock Frontier();

    /// <summary>
    /// Every record this store holds that the peer (described by <paramref name="peerFrontier"/>) is
    /// missing — i.e. whose origin sequence exceeds the peer's entry for that origin.
    /// </summary>
    IReadOnlyList<SyncRecord> RecordsSince(VectorClock peerFrontier);

    /// <summary>
    /// Apply an incoming record. Returns true if it was new, false if already present (a no-op).
    /// Must be idempotent: applying the same (origin, sequence) twice changes nothing.
    /// </summary>
    bool Apply(SyncRecord record);
}
