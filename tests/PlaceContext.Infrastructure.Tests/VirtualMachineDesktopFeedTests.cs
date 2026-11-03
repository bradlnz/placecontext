using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Sockets;
using System.Net;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public sealed class VirtualMachineDesktopFeedTests
{
    internal static byte[] DecodeFrame(byte[] png)
    {
        using var encoded = new MemoryStream();
        for (var offset = 8; offset < png.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset)));
            if (png.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
            {
                encoded.Write(png, offset + 8, length);
            }
            offset += length + 12;
        }
        encoded.Position = 0;
        using var zip = new ZLibStream(encoded, CompressionMode.Decompress);
        var pixels = new byte[800 * (1 + 1280 * 3)];
        zip.ReadExactly(pixels);
        Assert.Equal(-1, zip.ReadByte());
        for (var row = 0; row < 800; row++)
        {
            Assert.Equal(0, pixels[row * (1 + 1280 * 3)]);
        }
        return pixels;
    }
    internal static (int X, int Y) FindColor(byte[] png, byte red, byte green, byte blue)
    {
        var pixels = DecodeFrame(png);
        long xSum = 0, ySum = 0, count = 0;
        for (var y = 0; y < 800; y++)
        {
            for (var x = 0; x < 1280; x++)
            {
                var offset = y * (1 + 1280 * 3) + 1 + x * 3;
                if (pixels[offset] == red && pixels[offset + 1] == green && pixels[offset + 2] == blue)
                {
                    xSum += x;
                    ySum += y;
                    count++;
                }
            }
        }
        Assert.True(count > 100, "Expected target is not visible in the live desktop frame.");
        return ((int)(xSum / count), (int)(ySum / count));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Persistent_feed_reads_current_pixels_and_rejects_out_of_bounds_updates(bool invalid)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, ct);
        using var server = await listener.AcceptTcpClientAsync(ct);
        var phase = 0;
        var serving = Task.Run(async () =>
        {
            var stream = server.GetStream();
            var buffer = new byte[32];
            try
            {
                await stream.WriteAsync("RFB 003.008\n"u8.ToArray(), ct);
                await stream.ReadExactlyAsync(buffer.AsMemory(0, 12), ct);
                await stream.WriteAsync(new byte[] { 1, 1 }, ct);
                await stream.ReadExactlyAsync(buffer.AsMemory(0, 1), ct);
                Assert.Equal(1, buffer[0]);
                await stream.WriteAsync(new byte[4], ct);
                await stream.ReadExactlyAsync(buffer.AsMemory(0, 1), ct);
                Assert.Equal(1, buffer[0]);
                var init = new byte[24];
                init[1] = 2;
                init[3] = 1;
                await stream.WriteAsync(init, ct);
                await stream.ReadExactlyAsync(buffer, ct);
                Assert.Equal(0, buffer[0]);
                Assert.Equal(2, buffer[20]);
                while (true)
                {
                    await stream.ReadExactlyAsync(buffer.AsMemory(0, 10), ct);
                    Assert.Equal(3, buffer[0]);
                    byte[] update = [0,0,0,1, 0,0,0,0, 0,(byte)(invalid ? 3 : 2),0,1, 0,0,0,0,
                        (byte)(phase == 0 ? 255 : 0),0,(byte)(phase == 0 ? 0 : 255),0, 0,255,0,0];
                    await stream.WriteAsync(update, ct);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }, ct);
        await using (var feed = await VirtualMachineDesktopFeed.StartAsync(client.GetStream(), ct))
        {
            async Task AssertFrameAsync()
            {
                if (invalid)
                {
                    await Assert.ThrowsAsync<IOException>(() => feed.ReadFrameAsync(ct));
                    return;
                }
                {
                    var first = DecodeFrame(await feed.ReadFrameAsync(ct));
                    Assert.Equal(new byte[] { 255, 0, 0 }, first[1..4]);
                    Assert.Equal(new byte[] { 0, 255, 0 }, first[(1 + 1279 * 3)..(4 + 1279 * 3)]);
                    phase = 1;
                    var next = DecodeFrame(await feed.ReadFrameAsync(ct));
                    Assert.Equal(new byte[] { 0, 0, 255 }, next[1..4]);
                }
            }
            await AssertFrameAsync();
        }
        server.Close();
        await serving;
    }
}
