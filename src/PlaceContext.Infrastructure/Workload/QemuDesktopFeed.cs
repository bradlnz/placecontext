using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Sockets;

namespace PlaceContext.Infrastructure.Workload;

// One bounded framebuffer per VM, read from the same private VNC display used by the live preview.
internal sealed class QemuDesktopFeed : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly CancellationTokenSource _stop;
    private readonly SemaphoreSlim _frame = new(1, 1);
    private readonly object _sync = new();
    private TaskCompletionSource _updated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _pump = Task.CompletedTask;
    private Exception? _failure;
    private byte[] _pixels = [];
    private int _width, _height;
    private long _requested, _completed;

    private QemuDesktopFeed(Stream stream, CancellationToken ct) { _stream = stream; _stop = CancellationTokenSource.CreateLinkedTokenSource(ct); }
    internal static async Task<QemuDesktopFeed> ConnectAsync(string path, CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), ct); }
        catch { socket.Dispose(); throw; }
        return await StartAsync(new NetworkStream(socket, ownsSocket: true), ct);
    }
    internal static async Task<QemuDesktopFeed> StartAsync(Stream stream, CancellationToken ct)
    {
        var feed = new QemuDesktopFeed(stream, ct);
        try
        {
            var version = new byte[12]; await stream.ReadExactlyAsync(version, ct);
            if (!version.AsSpan().SequenceEqual("RFB 003.008\n"u8)) throw new IOException("Unsupported live desktop protocol.");
            await stream.WriteAsync(version, ct);
            var count = new byte[1]; await stream.ReadExactlyAsync(count, ct);
            var security = new byte[count[0]]; await stream.ReadExactlyAsync(security, ct);
            if (!security.Contains((byte)1)) throw new IOException("Private desktop authentication is unavailable.");
            await stream.WriteAsync(new byte[] { 1 }, ct);
            var result = new byte[4]; await stream.ReadExactlyAsync(result, ct);
            if (BinaryPrimitives.ReadUInt32BigEndian(result) != 0) throw new IOException("Private desktop connection rejected.");
            await stream.WriteAsync(new byte[] { 1 }, ct); // Shared: opening the preview must not disconnect automation.
            var init = new byte[24]; await stream.ReadExactlyAsync(init, ct);
            feed.Resize(BinaryPrimitives.ReadUInt16BigEndian(init), BinaryPrimitives.ReadUInt16BigEndian(init.AsSpan(2)));
            var nameLength = BinaryPrimitives.ReadUInt32BigEndian(init.AsSpan(20));
            if (nameLength > 4096) throw new IOException("Invalid desktop name length.");
            await stream.ReadExactlyAsync(new byte[(int)nameLength], ct);
            // Little-endian RGBX, raw updates and desktop resize notifications only (RFC 6143).
            await stream.WriteAsync(new byte[] { 0,0,0,0, 32,24,0,1, 0,255,0,255,0,255, 0,8,16,0,0,0,
                2,0,0,2, 0,0,0,0, 255,255,255,33 }, ct);
            feed._pump = feed.PumpAsync();
            return feed;
        }
        catch { await feed.DisposeAsync(); throw; }
    }
    private void Resize(int width, int height)
    {
        if (width is < 1 or > 4096 || height is < 1 or > 2160) throw new IOException("Live desktop dimensions exceed the limit.");
        _pixels = new byte[checked(width * height * 4)]; _width = width; _height = height;
    }
    private async Task RequestAsync(CancellationToken ct)
    {
        byte[] request = [3,0,0,0,0,0,0,0,0,0];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6), (ushort)_width);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8), (ushort)_height);
        lock (_sync) _requested++;
        await _stream.WriteAsync(request, ct);
    }
    private async Task PumpAsync()
    {
        var ct = _stop.Token;
        try
        {
            await RequestAsync(ct);
            var header = new byte[12];
            while (true)
            {
                await _stream.ReadExactlyAsync(header.AsMemory(0, 1), ct);
                if (header[0] == 2) continue; // Bell.
                if (header[0] == 3)
                {
                    await _stream.ReadExactlyAsync(header.AsMemory(0, 7), ct);
                    var length = Math.Abs((long)BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(3, 4)));
                    if (length > 1_048_576) throw new IOException("Desktop clipboard exceeds the limit.");
                    await _stream.ReadExactlyAsync(new byte[(int)length], ct);
                    continue; // Never send clipboard contents to a model or replay.
                }
                if (header[0] != 0) throw new IOException("Unexpected live desktop message.");
                await _stream.ReadExactlyAsync(header.AsMemory(0, 3), ct);
                var rectangles = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(1, 2));
                if (rectangles > 4096) throw new IOException("Too many desktop updates.");
                var resized = false;
                await _frame.WaitAsync(ct);
                try
                {
                    for (var rectangle = 0; rectangle < rectangles; rectangle++)
                    {
                        await _stream.ReadExactlyAsync(header, ct);
                        var x = BinaryPrimitives.ReadUInt16BigEndian(header);
                        var y = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                        var width = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                        var height = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6));
                        var encoding = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8));
                        if (encoding == -223) { Resize(width, height); resized = true; continue; }
                        if (encoding != 0 || x + width > _width || y + height > _height) throw new IOException("Invalid live desktop rectangle.");
                        for (var row = 0; row < height; row++)
                            await _stream.ReadExactlyAsync(_pixels.AsMemory(((y + row) * _width + x) * 4, width * 4), ct);
                    }
                }
                finally { _frame.Release(); }
                if (!resized)
                    lock (_sync) { _completed = _requested; _updated.TrySetResult(); _updated = new(TaskCreationOptions.RunContinuationsAsynchronously); }
                // Bound the automation feed to five frames/second; only model decisions encode/store an image.
                await Task.Delay(200, ct);
                await RequestAsync(ct);
            }
        }
        catch (Exception ex)
        {
            lock (_sync) { _failure = ex; _updated.TrySetException(new IOException("The live desktop feed ended.", ex)); }
        }
    }
    internal async Task<byte[]> ReadFrameAsync(CancellationToken ct)
    {
        long after;
        lock (_sync) after = _requested;
        while (true)
        {
            Task next;
            lock (_sync)
            {
                if (_failure is not null) throw new IOException("The live desktop feed ended.", _failure);
                if (_completed > after) break;
                next = _updated.Task;
            }
            await next.WaitAsync(ct);
        }
        byte[] pixels; int width, height;
        await _frame.WaitAsync(ct);
        try { pixels = (byte[])_pixels.Clone(); width = _width; height = _height; }
        finally { _frame.Release(); }
        return EncodePng(pixels, width, height);
    }
    internal static byte[] EncodePng(byte[] pixels, int width, int height)
    {
        if (width < 1 || height < 1 || pixels.Length != checked(width * height * 4)) throw new ArgumentException("Invalid framebuffer.");
        using var output = new MemoryStream(); output.Write(new byte[] {137,80,78,71,13,10,26,10});
        var ihdr = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(ihdr, 1280); BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 800);
        ihdr[8] = 8; ihdr[9] = 2; WriteChunk(output, "IHDR"u8, ihdr);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            var row = new byte[1 + 1280 * 3];
            for (var y = 0; y < 800; y++)
            {
                for (var x = 0; x < 1280; x++)
                    pixels.AsSpan(((y * height / 800) * width + x * width / 1280) * 4, 3).CopyTo(row.AsSpan(1 + x * 3));
                zlib.Write(row);
            }
        }
        WriteChunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }
    private static void WriteChunk(Stream output, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number); output.Write(kind); output.Write(data);
        uint crc = uint.MaxValue;
        foreach (var value in kind) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        foreach (var value in data) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
    }
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(value =>
    {
        var crc = (uint)value;
        for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        return crc;
    }).ToArray();
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync(); await _stream.DisposeAsync();
        await _pump; _stop.Dispose(); _frame.Dispose();
    }
}
