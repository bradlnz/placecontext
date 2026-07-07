using PlaceContext.Sync;

namespace PlaceContext.Sync.Tests;

public class SyncSessionTests
{
    private static NodeId Id(byte seed) =>
        NodeId.FromPublicKey(Enumerable.Range(0, 32).Select(i => (byte)(seed + i)).ToArray());

    private static SyncRecord Rec(NodeId origin, long seq) =>
        SyncRecord.Create(origin, seq, Guid.NewGuid(), "activity", $"d{seq}", new byte[] { (byte)seq });

    /// <summary>Deliver messages between two sessions until both inboxes drain.</summary>
    private static void Pump(SyncSession a, SyncSession b)
    {
        var toB = new Queue<Message>(a.Start());
        var toA = new Queue<Message>(b.Start());
        while (toA.Count > 0 || toB.Count > 0)
        {
            if (toB.Count > 0)
                foreach (var r in b.Receive(toB.Dequeue())) toA.Enqueue(r);
            else
                foreach (var r in a.Receive(toA.Dequeue())) toB.Enqueue(r);
        }
    }

    private static IReadOnlyList<SyncRecord> All(InMemorySyncStore store) => store.RecordsSince(VectorClock.Empty);

    [Fact]
    public void Two_nodes_converge_to_the_same_records()
    {
        var a = Id(1);
        var b = Id(2);
        var storeA = new InMemorySyncStore(new[] { Rec(a, 1), Rec(a, 2) });
        var storeB = new InMemorySyncStore(new[] { Rec(b, 1) });

        var sessionA = new SyncSession(a, storeA);
        var sessionB = new SyncSession(b, storeB);

        Pump(sessionA, sessionB);

        Assert.Equal(SessionState.Established, sessionA.State);
        Assert.Equal(SessionState.Established, sessionB.State);
        Assert.Equal(b, sessionA.PeerId);
        Assert.Equal(a, sessionB.PeerId);

        // Both stores now hold every record (A:1, A:2, B:1).
        Assert.Equal(3, storeA.Count);
        Assert.Equal(3, storeB.Count);
        Assert.Equal(storeA.Frontier(), storeB.Frontier());
        Assert.True(sessionA.IsConverged);
        Assert.True(sessionB.IsConverged);
    }

    [Fact]
    public void Re_syncing_already_converged_stores_exchanges_no_records_and_stays_converged()
    {
        var a = Id(1);
        var b = Id(2);
        var shared = new[] { Rec(a, 1), Rec(b, 1) };
        var storeA = new InMemorySyncStore(shared);
        var storeB = new InMemorySyncStore(shared);

        Pump(new SyncSession(a, storeA), new SyncSession(b, storeB));

        Assert.Equal(2, storeA.Count);
        Assert.Equal(2, storeB.Count);
        Assert.Equal(storeA.Frontier(), storeB.Frontier());
    }

    [Fact]
    public void One_sided_sync_delivers_the_delta()
    {
        var a = Id(1);
        var b = Id(2);
        var storeA = new InMemorySyncStore(new[] { Rec(a, 1), Rec(a, 2), Rec(a, 3) });
        var storeB = new InMemorySyncStore(); // empty

        Pump(new SyncSession(a, storeA), new SyncSession(b, storeB));

        Assert.Equal(3, storeB.Count);
        Assert.Equal(All(storeA).Select(r => r.Key).OrderBy(k => k),
                     All(storeB).Select(r => r.Key).OrderBy(k => k));
    }

    [Fact]
    public void Incompatible_major_version_closes_with_bye()
    {
        var a = Id(1);
        var session = new SyncSession(a, new InMemorySyncStore(), new ProtocolVersion(1, 0));
        session.Start();

        var peerHello = new HelloMessage(new ProtocolVersion(2, 0), Id(2), Array.Empty<string>(), VectorClock.Empty);
        var reply = session.Receive(peerHello);

        var bye = Assert.IsType<ByeMessage>(Assert.Single(reply));
        Assert.Contains("version", bye.Reason);
        Assert.Equal(SessionState.Closed, session.State);
    }

    [Fact]
    public void Duplicate_push_is_idempotent()
    {
        var a = Id(1);
        var b = Id(2);
        var storeB = new InMemorySyncStore();
        var session = new SyncSession(b, storeB);
        session.Start();
        session.Receive(new HelloMessage(ProtocolVersion.Current, a, Array.Empty<string>(), VectorClock.Empty));

        var push = new PushMessage(new[] { Rec(a, 1), Rec(a, 2) });
        var firstAck = Assert.IsType<AckMessage>(Assert.Single(session.Receive(push)));
        var secondAck = Assert.IsType<AckMessage>(Assert.Single(session.Receive(push)));

        Assert.Equal(2, storeB.Count);                      // no duplicates
        Assert.Equal(firstAck.Frontier, secondAck.Frontier); // same frontier both times
    }

    [Fact]
    public void Protocol_violations_throw()
    {
        var session = new SyncSession(Id(1), new InMemorySyncStore());

        // Push before the handshake is illegal.
        Assert.Throws<InvalidOperationException>(() => session.Receive(new PushMessage(Array.Empty<SyncRecord>())));

        // Start twice is illegal.
        session.Start();
        Assert.Throws<InvalidOperationException>(() => session.Start());
    }
}
