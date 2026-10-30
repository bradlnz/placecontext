using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Sockets;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public sealed class QemuDesktopSessionTests
{
    [Fact]
    public async Task Handoff_waits_for_a_checkpoint_and_resuming_discards_the_pending_agent_action()
    {
        var sessions = new QemuDesktopSessions();
        var tenant = Guid.NewGuid(); var user = Guid.NewGuid();
        var run = new QemuRunContext(Guid.NewGuid(), Guid.NewGuid(), 2);
        using var session = sessions.Register(tenant, user, run, "/unused", false, (_, _, _) => Task.CompletedTask);
        Assert.Null(sessions.Find(Guid.NewGuid(), user, run));
        Assert.Null(sessions.Find(tenant, Guid.NewGuid(), run));
        Assert.Null(sessions.Find(tenant, user, run with { ProjectId = Guid.NewGuid() }));
        Assert.Equal("continue", await session.CheckpointAsync(default));
        session.TakeControl();
        Assert.Equal("Pausing", session.Status.State);
        Assert.Throws<InvalidOperationException>(session.Resume);
        var checkpoint = session.CheckpointAsync(default);
        Assert.Equal("Human", session.Status.State);
        Assert.False(checkpoint.IsCompleted);
        session.Resume();
        Assert.Equal("resumed", await checkpoint);
        Assert.Equal("continue", await session.CheckpointAsync(default));
        sessions.Remove(tenant, run);
        Assert.Null(sessions.Find(tenant, user, run));
    }
    [Fact]
    public async Task Login_save_is_explicit_stays_paused_and_cannot_race_resume()
    {
        byte[]? saved = null;
        using var session = new QemuDesktopSessions.Session(Guid.NewGuid(), Guid.NewGuid(), "/unused", true,
            (name, bytes, _) => { Assert.Equal("Staging", name); saved = bytes; return Task.CompletedTask; });
        var checkpoint = session.CheckpointAsync(default);
        var saving = session.SaveAsync("Staging", default);
        Assert.Equal("save", await checkpoint);
        Assert.Throws<InvalidOperationException>(session.Resume);
        await session.SaveProfileAsync([1, 2, 3], default);
        await saving;
        Assert.Equal(new byte[] { 1, 2, 3 }, saved);
        Assert.Equal("Human", session.Status.State);
        checkpoint = session.CheckpointAsync(default);
        Assert.False(checkpoint.IsCompleted);
        session.Resume();
        Assert.Equal("resumed", await checkpoint);
    }
    [Fact]
    public async Task Closing_a_run_cancels_pending_human_waits_and_saves()
    {
        using var session = new QemuDesktopSessions.Session(Guid.NewGuid(), Guid.NewGuid(), "/unused", true, (_, _, _) => Task.CompletedTask);
        var checkpoint = session.CheckpointAsync(default);
        session.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checkpoint);
    }
    [Fact]
    public async Task Resume_revokes_the_live_console_and_prevents_further_input()
    {
        var path = Path.Combine(Path.GetTempPath(), "qemu-test-" + Guid.NewGuid().ToString("N"));
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path)); listener.Listen(1);
        try
        {
            using var session = new QemuDesktopSessions.Session(Guid.NewGuid(), Guid.NewGuid(), path, true, (_, _, _) => Task.CompletedTask);
            var checkpoint = session.CheckpointAsync(default);
            await using var console = await session.ConnectAsync(default);
            using var server = await listener.AcceptAsync();
            await console.WriteAsync(new byte[] { 1 });
            var buffer = new byte[1]; Assert.Equal(1, await server.ReceiveAsync(buffer));
            session.Resume();
            Assert.Equal("resumed", await checkpoint);
            await Assert.ThrowsAnyAsync<Exception>(async () => await console.WriteAsync(new byte[] { 2 }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ConnectAsync(default));
            await using var preview = await session.ConnectAsync(default, viewOnly: true);
            using var previewServer = await listener.AcceptAsync();
            await previewServer.SendAsync(new byte[] { 7 });
            await preview.ReadExactlyAsync(buffer);
            Assert.Equal(7, buffer[0]);
        }
        finally { listener.Dispose(); File.Delete(path); }
    }

    internal static byte[] Archive(string path = "./Default/Cookies", TarEntryType type = TarEntryType.RegularFile)
    {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionLevel.Fastest, true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            var entry = new PaxTarEntry(type, path);
            if (type == TarEntryType.RegularFile) entry.DataStream = new MemoryStream("test-session"u8.ToArray());
            else if (type == TarEntryType.SymbolicLink) entry.LinkName = "/etc/passwd";
            tar.WriteEntry(entry);
        }
        return bytes.ToArray();
    }
    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("Default/../../escape")]
    [InlineData("Default\\escape")]
    public void Browser_archives_reject_paths_outside_the_profile(string path) => Assert.Throws<ArgumentException>(() => QemuProfileArchive.Validate(Archive(path)));
    [Fact]
    public void Browser_archives_accept_regular_files_but_reject_links_and_oversized_input()
    {
        QemuProfileArchive.Validate(Archive());
        Assert.Throws<ArgumentException>(() => QemuProfileArchive.Validate(Archive("./link", TarEntryType.SymbolicLink)));
        Assert.Throws<ArgumentException>(() => QemuProfileArchive.Validate(new byte[QemuProfileArchive.MaxBytes + 1]));
    }
}
