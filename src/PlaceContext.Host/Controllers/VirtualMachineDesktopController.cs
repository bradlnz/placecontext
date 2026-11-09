using System.Buffers.Binary;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Workload.VirtualMachines;
namespace PlaceContext.Host.Controllers;

[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = Permission.JobsRun)]
public sealed class VirtualMachineDesktopController(IVirtualMachineDesktop desktop) : ControllerBase
{
    [HttpGet("/projects/{projectId:guid}/workflow-runs/{runId:guid}/qemu/{step:int}/desktop")]
    public async Task Connect(Guid projectId, Guid runId, int step, bool viewOnly = true)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!HttpContext.WebSockets.IsWebSocketRequest || !SameOrigin(Request.Headers.Origin, Request.Scheme, Request.Host.Value ?? ""))
        {
            Response.StatusCode = 403;
            return;
        }
        Stream stream;
        try
        {
            stream = await desktop.ConnectAsync(new(projectId, runId, step), HttpContext.RequestAborted, viewOnly);
        }
        catch (UnauthorizedAccessException)
        {
            Response.StatusCode = 403;
            return;
        }
        catch (InvalidOperationException)
        {
            Response.StatusCode = 409;
            return;
        }
        await using (stream)
        {
            using (var socket = await HttpContext.WebSockets.AcceptWebSocketAsync())
            {
                using (var stop = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted))
                {
                    var outgoing = SendAsync(stream, socket, stop.Token);
                    var incoming = viewOnly ? ForwardPreviewAsync(ReadClientAsync, stream, stop.Token) : ReceiveAsync(socket, stream, stop.Token);
                    async ValueTask ReadClientAsync(Memory<byte> buffer, CancellationToken ct)
                    {
                        while (!buffer.IsEmpty)
                        {
                            var frame = await socket.ReceiveAsync(buffer, ct);
                            if (frame.MessageType != WebSocketMessageType.Binary)
                            {
                                throw new IOException("Desktop connection closed.");
                            }
                            buffer = buffer[frame.Count..];
                        }
                    }
                    try
                    {
                        await Task.WhenAny(outgoing, incoming);
                    }
                    finally
                    {
                        await stop.CancelAsync();
                        socket.Abort();
                        try
                        {
                            await Task.WhenAll(outgoing, incoming);
                        }
                        catch (Exception ex) when (ex is OperationCanceledException or IOException or WebSocketException or ObjectDisposedException) { }
                    }
                }
            }
        }
    }
    // Enforce preview mode on the server; a modified browser must not inject keyboard, pointer or clipboard input.
    internal static async Task ForwardPreviewAsync(Func<Memory<byte>, CancellationToken, ValueTask> read,
        Stream desktop, CancellationToken ct)
    {
        var header = new byte[20];
        await read(header.AsMemory(0, 12), ct);
        if (!header.AsSpan(0, 12).SequenceEqual("RFB 003.008\n"u8))
        {
            throw new IOException("Unsupported desktop protocol.");
        }
        await desktop.WriteAsync(header.AsMemory(0, 12), ct);
        await read(header.AsMemory(0, 1), ct);
        if (header[0] != 1)
        {
            throw new IOException("Unsupported desktop security negotiation.");
        }
        await desktop.WriteAsync(header.AsMemory(0, 1), ct);
        await read(header.AsMemory(0, 1), ct);
        if (header[0] > 1)
        {
            throw new IOException("Invalid desktop initialization.");
        }
        await desktop.WriteAsync(header.AsMemory(0, 1), ct);
        while (true)
        {
            await read(header.AsMemory(0, 1), ct);
            var length = header[0] switch
            {
                0 => 20,
                2 => 4,
                3 or 150 => 10,
                248 => 9,
                6 => 8,
                _ => throw new IOException("Desktop preview is locked.")
            };
            await read(header.AsMemory(1, length - 1), ct);
            var extra = header[0] switch
            {
                2 => BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2)) * 4L,
                248 => header[8],
                6 => Math.Abs((long)BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4, 4))),
                _ => 0L
            };
            if (extra > 1_048_576)
            {
                throw new IOException("Desktop message exceeds the limit.");
            }
            var payload = new byte[(int)extra];
            if (extra > 0)
            {
                await read(payload, ct);
            }            // noVNC negotiates clipboard extensions even in view-only mode; consume without forwarding them.
            if (header[0] == 6)
            {
                continue;
            }
            await desktop.WriteAsync(header.AsMemory(0, length), ct);
            if (extra > 0)
            {
                await desktop.WriteAsync(payload, ct);
            }
        }
    }
    internal static bool SameOrigin(string? origin, string scheme, string host) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0
        && string.Equals(uri.GetLeftPart(UriPartial.Authority), $"{scheme}://{host}", StringComparison.OrdinalIgnoreCase);
    private static async Task SendAsync(Stream stream, WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[65536];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await socket.SendAsync(buffer.AsMemory(0, count), WebSocketMessageType.Binary, true, ct);
        }
    }
    private static async Task ReceiveAsync(WebSocket socket, Stream stream, CancellationToken ct)
    {
        var buffer = new byte[65536];
        var messageBytes = 0;
        while (!ct.IsCancellationRequested)
        {
            var frame = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (frame.MessageType != WebSocketMessageType.Binary || (messageBytes += frame.Count) > 1_048_576)
            {
                return;
            }
            await stream.WriteAsync(buffer.AsMemory(0, frame.Count), ct);
            if (frame.EndOfMessage)
            {
                messageBytes = 0;
            }
        }
    }
}
