using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Workload;

public sealed class QemuEnvironmentRunner(IOptions<QemuEnvironmentOptions> options, IQemuTaskAgent? agent = null) : IQemuEnvironmentRunner, IQemuEnvironmentCatalog
{
    public QemuEnvironmentCatalog Get() => new(options.Value.Enabled, options.Value.Images
        .OrderBy(image => image.Key, StringComparer.Ordinal)
        .Select(image => new QemuImageProfile(image.Key, Path.IsPathFullyQualified(image.Value) && File.Exists(image.Value))).ToArray(),
        agent is not null && agent.ConfigurationError is null, agent?.Model);

    // Bound memory consumption on each host independently of the chain worker count.
    private readonly SemaphoreSlim _capacity = new(1, 1);
    private const int MaxOutput = 65_536;

    public async Task<QemuEnvironmentResult> RunAsync(QemuChainAction action, string? input, CancellationToken ct,
        Func<QemuReplayCapture, CancellationToken, Task>? recordFrame = null)
    {
        var config = options.Value;
        if (!config.Enabled) throw new InvalidOperationException("QEMU steps are not enabled on this host. Configure PlaceContext:Qemu and a prepared image profile.");
        if (!config.Images.TryGetValue(action.ImageProfile, out var image) || !Path.IsPathFullyQualified(image) || !File.Exists(image))
            throw new InvalidOperationException($"QEMU image profile '{action.ImageProfile}' is not available on this host.");
        if (Encoding.UTF8.GetByteCount(input ?? "{}") > 1_048_576)
            throw new InvalidOperationException("QEMU input must be no larger than 1 MiB.");

        if (agent is null) throw new InvalidOperationException("Configure a QEMU task agent before running prompt instructions.");
        if (agent.ConfigurationError is { } agentError) throw new InvalidOperationException(agentError);

        await _capacity.WaitAsync(ct);
        var directory = Path.Combine(Path.GetTempPath(), "pcq-" + Guid.NewGuid().ToString("N"));
        Process? process = null;
        Task<string>? errors = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(action.TimeoutSeconds));
        try
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var socketPath = Path.Combine(directory, "agent.sock");
            var start = BuildStartInfo(config, action, image, socketPath);
            process = Process.Start(start) ?? throw new InvalidOperationException("Could not start QEMU.");
            errors = CaptureErrorsAsync(process.StandardError);
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException("QEMU exited during startup: " + await errors);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), deadline.Token); break; }
                catch (SocketException) { await Task.Delay(250, deadline.Token); }
            }
            using var stream = new NetworkStream(socket);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            // The socket accepts connections before the guest agent has booted; its reply is our readiness signal.
            await CallAsync(reader, writer, "guest-ping", new { }, deadline.Token);
            return await agent.RunAsync(action.Command, input, async (command, limit, token) =>
            {
                var execution = await CallAsync(reader, writer, "guest-exec", BuildGuestExecution(command, input), token);
                var pid = execution.GetProperty("pid").GetInt64();
                while (true)
                {
                    await Task.Delay(250, token);
                    if (process.HasExited) throw new InvalidOperationException("QEMU stopped before the agent action completed: " + await errors);
                    var result = await CallAsync(reader, writer, "guest-exec-status", new { pid }, token);
                    if (!result.GetProperty("exited").GetBoolean()) continue;
                    var stdout = DecodeOutput(result, "out-data");
                    var stderr = DecodeOutput(result, "err-data");
                    var truncated = stdout.Length > limit || stderr.Length > limit
                        || result.TryGetProperty("out-truncated", out var ot) && ot.GetBoolean()
                        || result.TryGetProperty("err-truncated", out var et) && et.GetBoolean();
                    return new QemuEnvironmentResult(result.TryGetProperty("exitcode", out var code) ? code.GetInt32() : -1,
                        stdout[..Math.Min(stdout.Length, limit)], stderr[..Math.Min(stderr.Length, limit)], truncated);
                }
            }, deadline.Token, recordFrame);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"QEMU step exceeded {action.TimeoutSeconds} seconds, including guest startup.");
        }
        finally
        {
            try
            {
                if (process is not null)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                    if (errors is not null) await errors;
                    process.Dispose();
                }
            }
            finally
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
                finally { _capacity.Release(); }
            }
        }
    }

    internal static ProcessStartInfo BuildStartInfo(QemuEnvironmentOptions options, QemuChainAction action, string image, string socketPath)
    {
        var start = new ProcessStartInfo(options.Executable) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] {
            "-no-user-config", "-nodefaults", "-display", "none", "-monitor", "none", "-serial", "none", "-no-reboot",
            "-machine", "q35", "-accel", options.UseKvm ? "kvm" : "tcg", "-m", action.MemoryMb.ToString(), "-smp", action.Cpus.ToString(),
            "-cpu", options.UseKvm ? "host" : "max",
            "-snapshot", "-drive", "file=" + image.Replace(",", ",,") + ",format=qcow2,if=virtio",
            "-device", "virtio-vga", "-device", "qemu-xhci", "-device", "usb-tablet",
            "-device", "virtio-rng-pci,max-bytes=1024,period=1000",
            "-device", "virtio-serial", "-chardev", "socket,path=" + socketPath.Replace(",", ",,") + ",server=on,wait=off,id=agent",
            "-device", "virtserialport,chardev=agent,name=org.qemu.guest_agent.0",
            "-nic", action.AllowNetwork ? "user,model=virtio-net-pci" : "none" }) start.ArgumentList.Add(argument);
        return start;
    }

    internal static Dictionary<string, object> BuildGuestExecution(string command, string? input) => new()
    {
        // QGA is privileged; drop credentials before interpreting any workflow-supplied text.
        ["path"] = "/usr/bin/setpriv",
        ["arg"] = new[] {
            "--reuid=workflow", "--regid=workflow", "--clear-groups", "--no-new-privs",
            "--inh-caps=-all", "--ambient-caps=-all", "--bounding-set=-all",
            "/usr/bin/env", "-i", "HOME=/home/workflow", "USER=workflow", "LOGNAME=workflow",
            "PATH=/usr/local/bin:/usr/bin:/bin", "DISPLAY=:0", "XAUTHORITY=/home/workflow/.Xauthority",
            "/bin/sh", "-c", """
                if [ "$(id -u)" = 0 ] || [ "$(id -g)" = 0 ]; then
                    echo 'The workflow desktop user must not be root.' >&2; exit 126
                fi
                cd "$HOME" || exit 126
                export XDG_RUNTIME_DIR="/run/user/$(id -u)"
                command -v xdpyinfo >/dev/null && command -v xprop >/dev/null || {
                    echo 'The image must provide an X11 desktop, xdpyinfo and xprop.' >&2; exit 126
                }
                until xdpyinfo >/dev/null 2>&1 && xprop -root _NET_SUPPORTING_WM_CHECK 2>/dev/null | grep -q 'window id #'; do sleep 1; done
                if [ -S "$XDG_RUNTIME_DIR/bus" ]; then export DBUS_SESSION_BUS_ADDRESS="unix:path=$XDG_RUNTIME_DIR/bus"; fi
                exec timeout --signal=TERM --kill-after=2 30 /bin/sh -c "$1"
                """, "workflow-task", command,
        },
        ["input-data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(input ?? "{}")),
        ["capture-output"] = true,
    };

    private static string DecodeOutput(JsonElement result, string name) => result.TryGetProperty(name, out var data)
        ? Encoding.UTF8.GetString(Convert.FromBase64String(data.GetString()!)) : "";

    private static async Task<JsonElement> CallAsync(StreamReader reader, StreamWriter writer, string command, object arguments, CancellationToken ct)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { execute = command, arguments }).AsMemory(), ct);
        // guest-exec captures at most 16 MiB per stream. Bound the protocol response as well.
        var line = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), ct);
            if (count == 0) throw new IOException("The QEMU guest agent disconnected.");
            line.Append(buffer, 0, count);
            if (line.Length > 48 * 1024 * 1024) throw new IOException("QEMU guest-agent response exceeded the size limit.");
            if (Array.IndexOf(buffer, '\n', 0, count) < 0) continue;
            using var document = JsonDocument.Parse(line.ToString());
            if (document.RootElement.TryGetProperty("error", out var error))
                throw new InvalidOperationException("QEMU guest agent: " + error.GetProperty("desc").GetString());
            return document.RootElement.GetProperty("return").Clone();
        }
    }

    private static async Task<string> CaptureErrorsAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
            if (text.Length < MaxOutput) text.Append(buffer, 0, Math.Min(count, MaxOutput - text.Length));
        return text.ToString();
    }
}
