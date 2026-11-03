using System.Buffers.Binary;
using PlaceContext.Host.Controllers;

namespace PlaceContext.Host.Tests;

public sealed class VirtualMachinePreviewProtocolTests
{
    private static byte[] Handshake => [.. "RFB 003.008\n"u8.ToArray(), 1, 1];
    [Fact]
    public async Task Preview_forwards_display_requests_but_never_clipboard_content()
    {
        byte[] encodings = [2, 0, 0, 1, 0, 0, 0, 0];
        byte[] update = [3, 1, 0, 0, 0, 0, 5, 0, 3, 32];
        byte[] clipboard = [6, 0, 0, 0, 0, 0, 0, 3, 97, 98, 99];
        using var client = new MemoryStream([.. Handshake, .. encodings, .. clipboard, .. update]);
        using var desktop = new MemoryStream();
        await Assert.ThrowsAsync<EndOfStreamException>(() => VirtualMachineDesktopController.ForwardPreviewAsync(client.ReadExactlyAsync, desktop, default));
        Assert.Equal([.. Handshake, .. encodings, .. update], desktop.ToArray());
    }
    [Theory]
    [InlineData(4)] // Keyboard
    [InlineData(5)] // Pointer
    [InlineData(255)] // Extended keyboard
    [InlineData(251)] // Desktop resize
    public async Task Locked_preview_rejects_input_even_from_a_modified_client(byte message)
    {
        using var client = new MemoryStream([.. Handshake, message]);
        using var desktop = new MemoryStream();
        await Assert.ThrowsAsync<IOException>(() => VirtualMachineDesktopController.ForwardPreviewAsync(client.ReadExactlyAsync, desktop, default));
        Assert.Equal(Handshake, desktop.ToArray());
    }
    [Fact]
    public async Task Clipboard_extensions_are_bounded_before_allocating_the_payload()
    {
        var clipboard = new byte[8];
        clipboard[0] = 6;
        BinaryPrimitives.WriteInt32BigEndian(clipboard.AsSpan(4), int.MinValue);
        using var client = new MemoryStream([.. Handshake, .. clipboard]);
        using var desktop = new MemoryStream();
        await Assert.ThrowsAsync<IOException>(() => VirtualMachineDesktopController.ForwardPreviewAsync(client.ReadExactlyAsync, desktop, default));
        Assert.Equal(Handshake, desktop.ToArray());
    }
}
