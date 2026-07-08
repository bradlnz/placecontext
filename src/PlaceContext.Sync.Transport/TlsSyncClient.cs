using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace PlaceContext.Sync.Transport;

/// <summary>What a completed (or converged-and-closed) dial returns.</summary>
/// <param name="Session">The finished protocol session (peer id, agreed version, final state).</param>
/// <param name="NegotiatedProtocol">The TLS version the channel actually ran on (always TLS 1.3).</param>
public sealed record TlsSyncResult(SyncSession Session, SslProtocols NegotiatedProtocol);

/// <summary>
/// The dialing half of the PCSP TLS binding. Connects to a peer, pins its identity — the TLS
/// handshake only succeeds if the certificate the peer presents hashes to <c>expectedPeer</c> —
/// presents this node's own certificate for mutual authentication, and syncs until converged.
/// </summary>
public static class TlsSyncClient
{
    public static async Task<TlsSyncResult> SyncWithAsync(
        string host,
        int port,
        NodeId expectedPeer,
        SyncTlsIdentity identity,
        ISyncStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedPeer);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(store);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, cancellationToken);

        await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
        await ssl.AuthenticateAsClientAsync(
            TlsPeerAuthentication.ClientOptions(identity, expectedPeer), cancellationToken);

        var session = await FramedSyncChannel.RunAsync(ssl, identity.NodeId, store, expectedPeer, cancellationToken);
        return new TlsSyncResult(session, ssl.SslProtocol);
    }
}
