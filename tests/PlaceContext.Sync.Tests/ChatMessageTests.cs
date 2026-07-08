using PlaceContext.Sync.Wire;

namespace PlaceContext.Sync.Tests;

public class ChatMessageTests
{
    [Fact]
    public void Chat_roundtrips_through_the_codec()
    {
        var chat = new ChatMessage("deploy is done — safe to restart", 1_752_000_000_000);

        var back = Assert.IsType<ChatMessage>(MessageCodec.DecodePayload(MessageCodec.EncodePayload(chat)));

        Assert.Equal(chat, back);
    }

    [Fact]
    public void A_sync_session_ignores_chat_messages()
    {
        var a = NodeId.FromPublicKey(new byte[] { 1 });
        var b = NodeId.FromPublicKey(new byte[] { 2 });
        var session = new SyncSession(a, new InMemorySyncStore());
        session.Start();
        session.Receive(new HelloMessage(ProtocolVersion.Current, b, Array.Empty<string>(), VectorClock.Empty));
        Assert.Equal(SessionState.Established, session.State);

        var outputs = session.Receive(new ChatMessage("ping", 1));

        Assert.Empty(outputs);
        Assert.Equal(SessionState.Established, session.State);
    }
}
