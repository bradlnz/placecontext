namespace PlaceContext.Sync;

/// <summary>
/// A Lamport clock for stamping locally-authored records with a strictly-increasing sequence, kept
/// ahead of anything observed from peers so causal order survives across nodes. Mutable by design
/// (it is the node's local counter); not a Value Object.
/// </summary>
public sealed class LamportClock
{
    private long _counter;

    public LamportClock(long start = 0)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "Clock must be non-negative.");
        _counter = start;
    }

    /// <summary>The last value handed out (0 before any tick).</summary>
    public long Current => _counter;

    /// <summary>Advance and return the next sequence for a locally-authored record.</summary>
    public long Next() => ++_counter;

    /// <summary>
    /// Fold in a sequence observed from a peer, keeping local time strictly ahead of it. Call this
    /// as records arrive so the next local <see cref="Next"/> never collides causally.
    /// </summary>
    public void Witness(long observed)
    {
        if (observed > _counter) _counter = observed;
    }
}
