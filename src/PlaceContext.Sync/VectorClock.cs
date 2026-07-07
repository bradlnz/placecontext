using System.Collections.Immutable;

namespace PlaceContext.Sync;

/// <summary>
/// A node's knowledge frontier: for each origin <see cref="NodeId"/>, the highest record sequence
/// seen from it. The compact summary of "what I have"; the delta a peer is missing is every record
/// whose origin sequence exceeds this clock's entry for that origin. Immutable Value Object —
/// mutating operations return a new clock.
/// </summary>
public sealed class VectorClock : IEquatable<VectorClock>
{
    private readonly ImmutableSortedDictionary<string, long> _entries;

    public static readonly VectorClock Empty =
        new(ImmutableSortedDictionary<string, long>.Empty.WithComparers(StringComparer.Ordinal));

    private VectorClock(ImmutableSortedDictionary<string, long> entries) => _entries = entries;

    public static VectorClock FromEntries(IEnumerable<KeyValuePair<NodeId, long>> entries)
    {
        var b = ImmutableSortedDictionary.CreateBuilder<string, long>(StringComparer.Ordinal);
        foreach (var (node, seq) in entries)
        {
            if (seq < 0) throw new ArgumentOutOfRangeException(nameof(entries), "Sequence must be non-negative.");
            if (seq > 0) b[node.Value] = seq; // 0 == "never seen"; omit to keep clocks canonical
        }
        return new VectorClock(b.ToImmutable());
    }

    /// <summary>The highest sequence seen from <paramref name="origin"/> (0 if never seen).</summary>
    public long SequenceFor(NodeId origin) => _entries.TryGetValue(origin.Value, out var s) ? s : 0;

    /// <summary>The origins this clock knows about.</summary>
    public IReadOnlyCollection<KeyValuePair<string, long>> Entries => _entries;

    /// <summary>
    /// Return a clock advanced to include (origin, sequence). A no-op if the sequence is not newer,
    /// which is what makes applying a record idempotent.
    /// </summary>
    public VectorClock Observe(NodeId origin, long sequence)
    {
        if (sequence <= SequenceFor(origin)) return this;
        return new VectorClock(_entries.SetItem(origin.Value, sequence));
    }

    /// <summary>Element-wise maximum with another clock — the union of both nodes' knowledge.</summary>
    public VectorClock Merge(VectorClock other)
    {
        var b = _entries.ToBuilder();
        foreach (var (k, v) in other._entries)
            if (!b.TryGetValue(k, out var cur) || v > cur) b[k] = v;
        return new VectorClock(b.ToImmutable());
    }

    /// <summary>
    /// True when this clock is at least as advanced as <paramref name="other"/> on every origin —
    /// i.e. this node has seen everything the other had. Convergence is mutual domination.
    /// </summary>
    public bool Dominates(VectorClock other)
    {
        foreach (var (k, v) in other._entries)
            if (SequenceFor(NodeIdKey(k)) < v) return false;
        return true;
    }

    private static NodeId NodeIdKey(string raw) => NodeId.Parse(raw);

    public bool Equals(VectorClock? other)
    {
        if (other is null || _entries.Count != other._entries.Count) return false;
        foreach (var (k, v) in _entries)
            if (!other._entries.TryGetValue(k, out var ov) || ov != v) return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as VectorClock);

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var (k, v) in _entries) { h.Add(k); h.Add(v); }
        return h.ToHashCode();
    }
}
