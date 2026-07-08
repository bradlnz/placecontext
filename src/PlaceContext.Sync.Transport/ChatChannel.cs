using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Threading.Channels;
using PlaceContext.Sync.Wire;

namespace PlaceContext.Sync.Transport;

/// <summary>One received chat line. <see cref="From"/> is the TLS-authenticated peer, never a claim.</summary>
public sealed record ChatLine(NodeId From, string Text, DateTimeOffset SentAt);

/// <summary>
/// A long-lived, duplex chat conversation between two nodes over an already-authenticated TLS 1.3
/// stream. Either side can <see cref="SendAsync"/> at any time; incoming lines arrive through
/// <see cref="ReadAllAsync"/>, which completes when the peer says Bye or hangs up. The sender of
/// every line is the certificate-authenticated <see cref="PeerId"/> — chat frames carry no sender
/// field to spoof.
/// </summary>
public sealed class ChatChannel : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly IDisposable? _socket;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Channel<ChatLine> _incoming = Channel.CreateUnbounded<ChatLine>();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readLoop;
    private bool _disposed;

    public NodeId PeerId { get; }
    public SslProtocols NegotiatedProtocol { get; }

    internal ChatChannel(Stream stream, NodeId peerId, SslProtocols negotiatedProtocol, IDisposable? socket)
    {
        _stream = stream;
        _socket = socket;
        PeerId = peerId;
        NegotiatedProtocol = negotiatedProtocol;
        _readLoop = ReadLoopAsync();
    }

    /// <summary>Send one line to the peer (stamped with the local clock).</summary>
    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var frame = MessageCodec.EncodeFrame(new ChatMessage(text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _stream.WriteAsync(frame, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Incoming lines, in arrival order, until the conversation ends.</summary>
    public IAsyncEnumerable<ChatLine> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _incoming.Reader.ReadAllAsync(cancellationToken);

    private async Task ReadLoopAsync()
    {
        var pending = new List<byte>();
        var chunk = new byte[16 * 1024];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int n = await _stream.ReadAsync(chunk, _cts.Token);
                if (n == 0) break;
                pending.AddRange(new ArraySegment<byte>(chunk, 0, n));

                while (MessageCodec.TryReadFrame(CollectionsMarshal.AsSpan(pending), out var message, out var consumed))
                {
                    pending.RemoveRange(0, consumed);
                    switch (message)
                    {
                        case ChatMessage chat:
                            _incoming.Writer.TryWrite(new ChatLine(
                                PeerId, chat.Text, DateTimeOffset.FromUnixTimeMilliseconds(chat.SentAtUnixMs)));
                            break;
                        case ByeMessage:
                            return;
                        // any other kind on a chat channel is ignored (forward compat, §7)
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { } // peer vanished — same outcome as an orderly Bye
        catch (WireFormatException) { } // garbage frames end the conversation, quietly
        finally
        {
            _incoming.Writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Best-effort goodbye so the peer's read loop completes promptly.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var bye = MessageCodec.EncodeFrame(new ByeMessage("closing"));
            await _writeLock.WaitAsync(timeout.Token);
            try
            {
                await _stream.WriteAsync(bye, timeout.Token);
                await _stream.FlushAsync(timeout.Token);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch { }

        _cts.Cancel();
        try { await _readLoop; } catch { }
        await _stream.DisposeAsync();
        _socket?.Dispose();
        _cts.Dispose();
        _writeLock.Dispose();
    }
}
