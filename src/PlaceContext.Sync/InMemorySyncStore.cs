namespace PlaceContext.Sync;

/// <summary>
/// A reference <see cref="ISyncStore"/> backed by an in-memory grow-only set keyed by
/// <see cref="SyncRecord.Key"/>. Used by tests and as the default before a durable (Postgres) store
/// is wired in. Not thread-safe; a session drives it single-threaded.
/// </summary>
public sealed class InMemorySyncStore : ISyncStore
{
    private readonly Dictionary<string, SyncRecord> _records = new();

    public InMemorySyncStore(IEnumerable<SyncRecord>? seed = null)
    {
        if (seed is null) return;
        foreach (var r in seed) Apply(r);
    }

    public int Count => _records.Count;

    public VectorClock Frontier()
    {
        var clock = VectorClock.Empty;
        foreach (var r in _records.Values)
            clock = clock.Observe(r.Origin, r.Sequence);
        return clock;
    }

    public IReadOnlyList<SyncRecord> RecordsSince(VectorClock peerFrontier)
    {
        var missing = new List<SyncRecord>();
        foreach (var r in _records.Values)
            if (r.Sequence > peerFrontier.SequenceFor(r.Origin))
                missing.Add(r);

        // Deterministic order: by origin, then sequence — keeps Push batches reproducible/testable.
        missing.Sort(static (a, b) =>
        {
            var o = string.CompareOrdinal(a.Origin.Value, b.Origin.Value);
            return o != 0 ? o : a.Sequence.CompareTo(b.Sequence);
        });
        return missing;
    }

    public bool Apply(SyncRecord record)
    {
        if (_records.ContainsKey(record.Key)) return false;
        _records[record.Key] = record;
        return true;
    }
}
