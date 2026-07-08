using System.Runtime.InteropServices;
using PlaceContext.Sync.Wire;

namespace PlaceContext.Sync.Transport;

/// <summary>
/// Drives one <see cref="SyncSession"/> over an already-secured byte stream: writes the opening
/// Hello, then reads frames, feeds them to the session, and writes whatever it returns, until the
/// session converges (send Bye) or the peer closes. Enforces the identity binding: the NodeId the
/// peer's Hello claims must be the one its TLS certificate certified, else the channel is torn down
/// before a single record is exchanged.
/// </summary>
public static class FramedSyncChannel
{
    public static async Task<SyncSession> RunAsync(
        Stream stream,
        NodeId localId,
        ISyncStore store,
        NodeId authenticatedPeer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(authenticatedPeer);

        var session = new SyncSession(localId, store);
        await WriteAsync(stream, session.Start(), cancellationToken);

        var pending = new List<byte>();
        var chunk = new byte[64 * 1024];
        while (session.State != SessionState.Closed)
        {
            if (session.IsConverged)
            {
                // Both frontiers dominate each other — orderly close. The peer may have hung up
                // first for the same reason; a write failure here is not an error.
                try { await WriteAsync(stream, session.Close("converged"), cancellationToken); }
                catch (IOException) { }
                break;
            }

            int n = await stream.ReadAsync(chunk, cancellationToken);
            if (n == 0)
            {
                session.Close("peer disconnected");
                break;
            }
            pending.AddRange(new ArraySegment<byte>(chunk, 0, n));

            while (session.State != SessionState.Closed
                   && MessageCodec.TryReadFrame(CollectionsMarshal.AsSpan(pending), out var message, out var consumed))
            {
                pending.RemoveRange(0, consumed);

                // Identity binding: a Hello may only claim the NodeId the transport authenticated.
                if (message is HelloMessage hello && hello.NodeId != authenticatedPeer)
                {
                    try { await WriteAsync(stream, session.Close("identity: Hello does not match the presented certificate"), cancellationToken); }
                    catch (IOException) { }
                    throw new SyncTransportException(
                        $"Peer's Hello claims NodeId {hello.NodeId} but its certificate certifies {authenticatedPeer}.");
                }

                await WriteAsync(stream, session.Receive(message!), cancellationToken);
            }
        }

        return session;
    }

    private static async Task WriteAsync(Stream stream, IReadOnlyList<Message> messages, CancellationToken ct)
    {
        if (messages.Count == 0) return;
        foreach (var message in messages)
            await stream.WriteAsync(MessageCodec.EncodeFrame(message), ct);
        await stream.FlushAsync(ct);
    }
}
