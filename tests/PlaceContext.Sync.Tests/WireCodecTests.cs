using PlaceContext.Sync;
using PlaceContext.Sync.Wire;

namespace PlaceContext.Sync.Tests;

public class WireCodecTests
{
    private static NodeId Id(byte seed) =>
        NodeId.FromPublicKey(Enumerable.Range(0, 32).Select(i => (byte)(seed + i)).ToArray());

    private static SyncRecord Rec(NodeId origin, long seq) =>
        SyncRecord.Create(origin, seq, Guid.NewGuid(), "activity", "deadbeef", new byte[] { 1, 2, 3, (byte)seq });

    [Fact]
    public void Hello_round_trips_through_a_frame()
    {
        var id = Id(1);
        var frontier = VectorClock.Empty.Observe(id, 7).Observe(Id(2), 3);
        var hello = new HelloMessage(new ProtocolVersion(1, 4), id, new[] { "delta", "compress" }, frontier);

        var frame = MessageCodec.EncodeFrame(hello);
        Assert.True(MessageCodec.TryReadFrame(frame, out var decoded, out var consumed));
        Assert.Equal(frame.Length, consumed);

        var back = Assert.IsType<HelloMessage>(decoded);
        Assert.Equal(new ProtocolVersion(1, 4), back.Version);
        Assert.Equal(id, back.NodeId);
        Assert.Equal(new[] { "delta", "compress" }, back.Capabilities);
        Assert.Equal(frontier, back.Frontier);
    }

    [Fact]
    public void Push_Ack_Bye_round_trip()
    {
        var origin = Id(1);
        var push = new PushMessage(new[] { Rec(origin, 1), Rec(origin, 2) });
        var ack = new AckMessage(VectorClock.Empty.Observe(origin, 2));
        var bye = new ByeMessage("done");

        foreach (var msg in new Message[] { push, ack, bye })
        {
            var frame = MessageCodec.EncodeFrame(msg);
            Assert.True(MessageCodec.TryReadFrame(frame, out var decoded, out _));
            Assert.Equal(msg.Kind, decoded!.Kind);
        }

        var decodedPush = (PushMessage)MessageCodec.DecodePayload(MessageCodec.EncodePayload(push));
        Assert.Equal(2, decodedPush.Records.Count);
        Assert.Equal(push.Records[0].Payload, decodedPush.Records[0].Payload);
        Assert.Equal(push.Records[1].ProjectId, decodedPush.Records[1].ProjectId);
    }

    [Fact]
    public void TryReadFrame_returns_false_until_the_whole_frame_has_arrived()
    {
        var frame = MessageCodec.EncodeFrame(new ByeMessage("partial-delivery-test"));

        // Every strict prefix is incomplete.
        for (int cut = 1; cut < frame.Length; cut++)
        {
            Assert.False(MessageCodec.TryReadFrame(frame.AsSpan(0, cut), out _, out var c));
            Assert.Equal(0, c);
        }

        Assert.True(MessageCodec.TryReadFrame(frame, out _, out var consumed));
        Assert.Equal(frame.Length, consumed);
    }

    [Fact]
    public void TryReadFrame_reads_one_frame_and_leaves_the_rest()
    {
        var first = MessageCodec.EncodeFrame(new ByeMessage("one"));
        var second = MessageCodec.EncodeFrame(new AckMessage(VectorClock.Empty));
        var stream = first.Concat(second).ToArray();

        Assert.True(MessageCodec.TryReadFrame(stream, out var m1, out var consumed));
        Assert.Equal(first.Length, consumed);
        Assert.IsType<ByeMessage>(m1);

        Assert.True(MessageCodec.TryReadFrame(stream.AsSpan(consumed), out var m2, out _));
        Assert.IsType<AckMessage>(m2);
    }

    [Fact]
    public void Truncated_payload_throws_wire_format()
    {
        var frame = MessageCodec.EncodeFrame(new ByeMessage("hello"));
        // Corrupt the length prefix to claim more than exists, then feed the full body region.
        Assert.Throws<WireFormatException>(() =>
            MessageCodec.DecodePayload(new byte[] { (byte)MessageKind.Bye, 0x05, (byte)'h' }));
    }
}
