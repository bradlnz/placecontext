using System.Net.Security;
using System.Net.Sockets;

namespace PlaceContext.Sync.Transport;

/// <summary>
/// The dialing half of node-to-node chat. Pins the peer's identity exactly like
/// <see cref="TlsSyncClient"/> — the handshake only succeeds against the certificate that hashes to
/// <c>expectedPeer</c> — and returns a live <see cref="ChatChannel"/> the caller owns.
/// </summary>
public static class TlsChatClient
{
    public static async Task<ChatChannel> ConnectAsync(
        string host,
        int port,
        NodeId expectedPeer,
        SyncTlsIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedPeer);
        ArgumentNullException.ThrowIfNull(identity);

        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port, cancellationToken);
            var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            try
            {
                await ssl.AuthenticateAsClientAsync(
                    TlsPeerAuthentication.ClientOptions(identity, expectedPeer), cancellationToken);
                return new ChatChannel(ssl, expectedPeer, ssl.SslProtocol, tcp);
            }
            catch
            {
                await ssl.DisposeAsync();
                throw;
            }
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }
}
