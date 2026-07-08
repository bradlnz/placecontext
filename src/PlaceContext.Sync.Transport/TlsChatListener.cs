using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace PlaceContext.Sync.Transport;

/// <summary>
/// The listening half of node-to-node chat: same mutual TLS 1.3 posture as sync (client certificate
/// mandatory, identity = certificate), but each authenticated connection becomes a long-lived
/// <see cref="ChatChannel"/> handed to <c>onConnected</c>. The handler owns the conversation; when
/// it returns, the channel is closed.
/// </summary>
public sealed class TlsChatListener : IAsyncDisposable
{
    private readonly TcpListener _tcp;
    private readonly SyncTlsIdentity _identity;
    private readonly Func<ChatChannel, Task> _onConnected;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    public int Port { get; }

    private TlsChatListener(TcpListener tcp, SyncTlsIdentity identity, Func<ChatChannel, Task> onConnected)
    {
        _tcp = tcp;
        _identity = identity;
        _onConnected = onConnected;
        Port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync();
    }

    /// <summary>Start listening. <paramref name="port"/> 0 picks a free port (read <see cref="Port"/>).</summary>
    public static TlsChatListener Start(
        SyncTlsIdentity identity, Func<ChatChannel, Task> onConnected, int port = 0, IPAddress? address = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(onConnected);
        var tcp = new TcpListener(address ?? IPAddress.Any, port);
        tcp.Start();
        return new TlsChatListener(tcp, identity, onConnected);
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
        SslStream ssl;
        try
        {
            ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsServerAsync(TlsPeerAuthentication.ServerOptions(_identity), _cts.Token);
        }
        catch
        {
            client.Dispose(); // a failed handshake is that connection's problem; keep accepting
            return;
        }

        var peer = SyncTlsIdentity.NodeIdFromCertificate(ssl.RemoteCertificate!);
        await using var channel = new ChatChannel(ssl, peer, ssl.SslProtocol, client);
        try
        {
            await _onConnected(channel);
        }
        catch
        {
            // the handler's failure must not kill the listener
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
