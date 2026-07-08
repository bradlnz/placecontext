using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace PlaceContext.Sync.Transport;

/// <summary>
/// The listening half of the PCSP TLS binding: accepts TCP connections, requires a mutually-
/// authenticated TLS 1.3 handshake (client certificate mandatory), derives the peer's NodeId from
/// the certificate it presented, and hands the encrypted stream to <see cref="FramedSyncChannel"/>.
/// A connection that cannot complete the handshake exchanges no application data — every PCSP byte
/// on the wire is inside TLS.
/// </summary>
public sealed class TlsSyncListener : IAsyncDisposable
{
    private readonly TcpListener _tcp;
    private readonly SyncTlsIdentity _identity;
    private readonly ISyncStore _store;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    public int Port { get; }

    private TlsSyncListener(TcpListener tcp, SyncTlsIdentity identity, ISyncStore store)
    {
        _tcp = tcp;
        _identity = identity;
        _store = store;
        Port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync();
    }

    /// <summary>Start listening. <paramref name="port"/> 0 picks a free port (read <see cref="Port"/>).</summary>
    public static TlsSyncListener Start(SyncTlsIdentity identity, ISyncStore store, int port = 0, IPAddress? address = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(store);
        var tcp = new TcpListener(address ?? IPAddress.Any, port);
        tcp.Start();
        return new TlsSyncListener(tcp, identity, store);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _tcp.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            _ = HandleAsync(client);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            await using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsServerAsync(TlsPeerAuthentication.ServerOptions(_identity), _cts.Token);

            var peer = SyncTlsIdentity.NodeIdFromCertificate(ssl.RemoteCertificate!);
            await FramedSyncChannel.RunAsync(ssl, _identity.NodeId, _store, peer, _cts.Token);
        }
        catch
        {
            // A failed handshake, a lying Hello, or a dropped socket is that connection's problem —
            // the listener keeps accepting.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _tcp.Stop();
        try { await _acceptLoop; }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
