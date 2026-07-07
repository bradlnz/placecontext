using PlaceContext.Sync;

namespace PlaceContext.Sync.Tests;

public class IdentityAndClockTests
{
    private static byte[] Key(byte seed) => Enumerable.Range(0, 32).Select(i => (byte)(seed + i)).ToArray();

    [Fact]
    public void NodeId_is_deterministic_self_certifying_and_round_trips()
    {
        var key = Key(1);
        var id = NodeId.FromPublicKey(key);

        Assert.Equal(32, id.Value.Length);
        Assert.Equal(id, NodeId.FromPublicKey(key));            // deterministic
        Assert.True(id.Certifies(key));                          // the key that made it certifies it
        Assert.False(id.Certifies(Key(2)));                      // a different key does not
        Assert.Equal(id, NodeId.Parse(id.Value));                // string round-trip
        Assert.Equal(id, NodeId.Parse(id.Value.ToLowerInvariant())); // case-insensitive
    }

    [Fact]
    public void NodeId_rejects_malformed_strings()
    {
        Assert.Throws<ArgumentException>(() => NodeId.Parse(""));
        Assert.Throws<ArgumentException>(() => NodeId.Parse("too-short"));
        Assert.Throws<ArgumentException>(() => NodeId.Parse(new string('I', 32))); // I not in alphabet
    }

    [Fact]
    public void LamportClock_advances_and_witnesses()
    {
        var c = new LamportClock();
        Assert.Equal(1, c.Next());
        Assert.Equal(2, c.Next());
        c.Witness(10);
        Assert.Equal(11, c.Next());   // jumped ahead of the observed 10
        c.Witness(5);                 // older observation ignored
        Assert.Equal(12, c.Next());
    }

    [Fact]
    public void VectorClock_observe_is_monotonic_and_idempotent()
    {
        var a = NodeId.FromPublicKey(Key(1));
        var clock = VectorClock.Empty.Observe(a, 3);
        Assert.Equal(3, clock.SequenceFor(a));
        Assert.Same(clock, clock.Observe(a, 2)); // no-op → same instance
        Assert.Equal(5, clock.Observe(a, 5).SequenceFor(a));
    }

    [Fact]
    public void VectorClock_merge_and_dominates()
    {
        var a = NodeId.FromPublicKey(Key(1));
        var b = NodeId.FromPublicKey(Key(2));

        var x = VectorClock.Empty.Observe(a, 3).Observe(b, 1);
        var y = VectorClock.Empty.Observe(a, 1).Observe(b, 4);

        var merged = x.Merge(y);
        Assert.Equal(3, merged.SequenceFor(a));
        Assert.Equal(4, merged.SequenceFor(b));

        Assert.True(merged.Dominates(x));
        Assert.True(merged.Dominates(y));
        Assert.False(x.Dominates(y)); // x behind on b
        Assert.True(VectorClock.Empty.Dominates(VectorClock.Empty));
    }
}
