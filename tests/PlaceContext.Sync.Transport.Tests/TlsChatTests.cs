using System.Net;
using System.Security.Authentication;
using PlaceContext.Sync;
using PlaceContext.Sync.Transport;

namespace PlaceContext.Sync.Transport.Tests;

public class TlsChatTests
{
    private static CancellationToken Deadline() => new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    [Fact]
    public async Task Nodes_exchange_chat_messages_both_ways_over_mutual_tls()
    {
        using var a = SyncTlsIdentity.Create();
        using var b = SyncTlsIdentity.Create();
        var ct = Deadline();

        // Node B answers every line and also speaks first.
        await using var listener = TlsChatListener.Start(b, async channel =>
        {
            await channel.SendAsync("welcome from B", ct);
            await foreach (var line in channel.ReadAllAsync(ct))
                await channel.SendAsync($"echo: {line.Text}", ct);
        }, address: IPAddress.Loopback);

        await using var chat = await TlsChatClient.ConnectAsync("127.0.0.1", listener.Port, b.NodeId, a, ct);

        Assert.Equal(SslProtocols.Tls13, chat.NegotiatedProtocol); // fully encrypted, nothing weaker
        Assert.Equal(b.NodeId, chat.PeerId);

        await chat.SendAsync("hello from A", ct);

        var received = new List<ChatLine>();
        await foreach (var line in chat.ReadAllAsync(ct))
        {
            received.Add(line);
            if (received.Count == 2) break;
        }

        // Sender identity comes from the TLS certificate, not from the message body.
        Assert.All(received, l => Assert.Equal(b.NodeId, l.From));
        Assert.Contains(received, l => l.Text == "welcome from B");
        Assert.Contains(received, l => l.Text == "echo: hello from A");
    }

    [Fact]
    public async Task Chat_with_the_wrong_expected_identity_is_refused()
    {
        using var a = SyncTlsIdentity.Create();
        using var b = SyncTlsIdentity.Create();
        using var somebodyElse = SyncTlsIdentity.Create();

        await using var listener = TlsChatListener.Start(b, _ => Task.CompletedTask, address: IPAddress.Loopback);

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            TlsChatClient.ConnectAsync("127.0.0.1", listener.Port, somebodyElse.NodeId, a, Deadline()));
    }

    [Fact]
    public async Task Closing_one_end_completes_the_other_ends_read_loop()
    {
        using var a = SyncTlsIdentity.Create();
        using var b = SyncTlsIdentity.Create();
        var ct = Deadline();

        var serverSawEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var listener = TlsChatListener.Start(b, async channel =>
        {
            await foreach (var _ in channel.ReadAllAsync(ct)) { }
            serverSawEnd.TrySetResult();
        }, address: IPAddress.Loopback);

        var chat = await TlsChatClient.ConnectAsync("127.0.0.1", listener.Port, b.NodeId, a, ct);
        await chat.DisposeAsync(); // sends Bye and hangs up

        await serverSawEnd.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
