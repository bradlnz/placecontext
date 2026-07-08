using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using PlaceContext.Sync;
using PlaceContext.Sync.Transport;
using PlaceContext.Sync.Wire;

namespace PlaceContext.Sync.Transport.Tests;

public class TlsSyncEndToEndTests
{
    private static CancellationToken Deadline() => new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private static SyncRecord Record(NodeId origin, long seq) =>
        SyncRecord.Create(origin, seq, Guid.NewGuid(), "activity", $"digest-{origin}-{seq}",
            System.Text.Encoding.UTF8.GetBytes($"payload {origin}/{seq}"));

    [Fact]
    public async Task Two_nodes_converge_over_mutual_tls()
    {
        using var a = SyncTlsIdentity.Create();
        using var b = SyncTlsIdentity.Create();
        var storeA = new InMemorySyncStore(new[] { Record(a.NodeId, 1), Record(a.NodeId, 2) });
        var storeB = new InMemorySyncStore(new[] { Record(b.NodeId, 1) });

        await using var listener = TlsSyncListener.Start(b, storeB, address: IPAddress.Loopback);
        var result = await TlsSyncClient.SyncWithAsync("127.0.0.1", listener.Port, b.NodeId, a, storeA, Deadline());

        // Fully encrypted: the channel must have negotiated TLS 1.3 — nothing weaker.
        Assert.Equal(SslProtocols.Tls13, result.NegotiatedProtocol);
        Assert.Equal(b.NodeId, result.Session.PeerId);

        // Converged: both nodes hold all three records.
        Assert.Equal(3, storeA.Count);
        Assert.Equal(3, storeB.Count);
        Assert.True(storeA.Frontier().Dominates(storeB.Frontier()));
        Assert.True(storeB.Frontier().Dominates(storeA.Frontier()));
    }

    [Fact]
    public async Task Dialing_with_the_wrong_expected_identity_is_refused_before_any_data_flows()
    {
        using var a = SyncTlsIdentity.Create();
        using var b = SyncTlsIdentity.Create();
        using var impostorExpectation = SyncTlsIdentity.Create();
        var storeA = new InMemorySyncStore(new[] { Record(a.NodeId, 1) });
        var storeB = new InMemorySyncStore();

        await using var listener = TlsSyncListener.Start(b, storeB, address: IPAddress.Loopback);

        // Client expects a different node's identity — the server's certificate cannot certify it.
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            TlsSyncClient.SyncWithAsync("127.0.0.1", listener.Port, impostorExpectation.NodeId, a, storeA, Deadline()));

        Assert.Equal(0, storeB.Count); // nothing leaked
    }

    [Fact]
    public async Task Server_refuses_clients_that_present_no_certificate()
    {
        using var b = SyncTlsIdentity.Create();
        var storeB = new InMemorySyncStore(new[] { Record(b.NodeId, 1) });

        await using var listener = TlsSyncListener.Start(b, storeB, address: IPAddress.Loopback);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, listener.Port, Deadline());
        await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: static (_, _, _, _) => true);

        // No client certificate: the handshake (or the first read, on TLS 1.3) must fail,
        // and the server must never send a PCSP frame.
        var received = 0;
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = b.NodeId.Value }, Deadline());
            var buf = new byte[1024];
            received = await ssl.ReadAsync(buf, Deadline());
        }
        catch (Exception e) when (e is AuthenticationException or IOException)
        {
            // expected: the server tore the connection down
        }

        Assert.Equal(0, received);
    }

    [Fact]
    public async Task Plaintext_connections_are_refused_and_the_listener_survives()
    {
        using var a = SyncTlsIdentity.Create();
        using var b = SyncTlsIdentity.Create();
        var storeA = new InMemorySyncStore(new[] { Record(a.NodeId, 1) });
        var storeB = new InMemorySyncStore();

        await using var listener = TlsSyncListener.Start(b, storeB, address: IPAddress.Loopback);

        // A cleartext PCSP Hello straight onto the socket — no TLS. The server must not sync.
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, listener.Port, Deadline());
            var stream = tcp.GetStream();
            var hello = new HelloMessage(ProtocolVersion.Current, a.NodeId, Array.Empty<string>(), storeA.Frontier());
            await stream.WriteAsync(MessageCodec.EncodeFrame(hello), Deadline());
            try
            {
                var buf = new byte[1024];
                while (await stream.ReadAsync(buf, Deadline()) > 0) { } // drain until the server hangs up
            }
            catch (IOException) { /* connection reset is fine too */ }
        }

        Assert.Equal(0, storeB.Count);

        // The failed handshake must not have killed the accept loop: a proper TLS sync still works.
        await TlsSyncClient.SyncWithAsync("127.0.0.1", listener.Port, b.NodeId, a, storeA, Deadline());
        Assert.Equal(1, storeB.Count);
    }

    [Fact]
    public async Task Hello_claiming_a_different_identity_than_the_certificate_is_rejected()
    {
        using var honest = SyncTlsIdentity.Create();
        using var claimed = SyncTlsIdentity.Create(); // the identity the liar pretends to be
        var store = new InMemorySyncStore(new[] { Record(honest.NodeId, 1) });

        // Drive the channel over a plain loopback socket pair: the "authenticated" peer is what the
        // TLS layer proved (claimed.NodeId), but the Hello arriving claims someone else — a lie.
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;

        using var liarTcp = new TcpClient();
        var connect = liarTcp.ConnectAsync(IPAddress.Loopback, port, Deadline()).AsTask();
        using var serverTcp = await tcpListener.AcceptTcpClientAsync(Deadline());
        await connect;
        tcpListener.Stop();

        var channelTask = FramedSyncChannel.RunAsync(
            serverTcp.GetStream(), honest.NodeId, store, authenticatedPeer: claimed.NodeId, Deadline());

        using var liar = SyncTlsIdentity.Create();
        var liarStream = liarTcp.GetStream();
        var lie = new HelloMessage(ProtocolVersion.Current, liar.NodeId, Array.Empty<string>(), VectorClock.Empty);
        await liarStream.WriteAsync(MessageCodec.EncodeFrame(lie), Deadline());

        await Assert.ThrowsAsync<SyncTransportException>(() => channelTask);
        Assert.Equal(1, store.Count); // nothing was applied from the liar
    }
}
