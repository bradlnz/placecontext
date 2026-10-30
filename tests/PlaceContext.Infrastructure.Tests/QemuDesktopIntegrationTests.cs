using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public sealed class QemuDesktopIntegrationTests
{
    // Test-only agent used to exercise the VM boundary independently of model behavior.
    private sealed class ScriptAgent : IQemuTaskAgent
    {
        public string? ConfigurationError => null;
        public string Model => "test-agent";
        public Task<QemuEnvironmentResult> RunAsync(string prompt, string? input,
            Func<string, int, CancellationToken, Task<QemuEnvironmentResult>> executeGuest, CancellationToken ct,
            Func<QemuReplayCapture, CancellationToken, Task>? recordFrame = null, QemuAgentInteraction? interaction = null, Func<CancellationToken, Task<byte[]>>? readLiveFrame = null) => executeGuest(prompt, 65536, ct);
    }
    private sealed class DesktopFactAttribute : FactAttribute
    {
        public DesktopFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_IMAGE")))
                Skip = "Set PLACECONTEXT_TEST_QEMU_IMAGE to a prepared desktop qcow2 image.";
        }
    }

    [DesktopFact]
    public async Task Desktop_runs_a_nonroot_browser_with_fresh_disks_and_cleans_up_on_timeout_and_cancellation()
    {
        var runner = new QemuEnvironmentRunner(Options.Create(new QemuEnvironmentOptions
        {
            Enabled = true,
            UseKvm = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_KVM") == "true",
            Images = new() { ["browser"] = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_IMAGE")! },
        }), new ScriptAgent());
        var directories = Directory.GetDirectories(Path.GetTempPath(), "pcq-*").Order().ToArray();
        var result = await runner.RunAsync(new("browser", """
            set -eu
            test "$(id -u)" != 0
            test "$(id -g)" != 0
            grep -Eq '^CapEff:[[:space:]]*0000000000000000$' /proc/self/status
            grep -Eq '^NoNewPrivs:[[:space:]]*1$' /proc/self/status
            if (cd /root) 2>/dev/null; then exit 1; fi
            test "$(cat)" = '{"hello":"world"}'
            test ! -e /home/workflow/run-marker
            touch /home/workflow/run-marker
            printf '<html><head><title>PlaceContext QEMU smoke</title></head><body>Desktop ready</body></html>' > /tmp/desktop-check.html
            chromium --disable-background-networking --disable-component-update --disable-extensions --disable-sync --no-default-browser-check --no-first-run --user-data-dir=/tmp/browser-profile file:///tmp/desktop-check.html > /tmp/browser.log 2>&1 &
            browser=$!
            trap 'kill "$browser" 2>/dev/null || true' EXIT
            count=0
            until xwininfo -root -tree | grep -q 'PlaceContext QEMU smoke'; do
                count=$((count + 1))
                if [ "$count" -gt 90 ]; then cat /tmp/browser.log >&2; exit 1; fi
                sleep 1
            done
            printf 'NONROOT_DESKTOP_BROWSER_OK\n'
            """, memoryMb: 2048, cpus: 2, timeoutSeconds: 240), "{\"hello\":\"world\"}", default);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Contains("NONROOT_DESKTOP_BROWSER_OK", result.Stdout);
        var fresh = await runner.RunAsync(new("browser", "test ! -e /home/workflow/run-marker", memoryMb: 2048, timeoutSeconds: 120), null, default);
        Assert.True(fresh.ExitCode == 0, fresh.Stderr);
        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(new("browser", "sleep 300", memoryMb: 2048, timeoutSeconds: 10), null, default));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(new("browser", "sleep 300", memoryMb: 2048), null, cancel.Token));
        Assert.Equal(directories, Directory.GetDirectories(Path.GetTempPath(), "pcq-*").Order().ToArray());
    }

    [DesktopFact]
    public async Task Prompt_agent_controls_a_real_desktop_observes_results_and_returns_a_summary()
    {
        const string command = """
            set -eu
            test "$(id -u)" != 0
            printf '<html><head><title>Agent desktop verification</title></head><body>Ready</body></html>' > /tmp/agent-check.html
            chromium --no-first-run --no-default-browser-check --disable-background-networking --user-data-dir=/tmp/agent-browser file:///tmp/agent-check.html >/tmp/agent-browser.log 2>&1 &
            count=0
            until xwininfo -root -tree | grep -q 'Agent desktop verification'; do
                count=$((count + 1))
                if [ "$count" -gt 20 ]; then cat /tmp/agent-browser.log >&2; exit 1; fi
                sleep 1
            done
            printf 'AGENT_BROWSER_VERIFIED\n'
            """;
        using var model = new QemuPromptAgentTests.ModelApi(
            QemuPromptAgentTests.Tool("guest_shell", new { command, description = "Open and verify the browser page" }),
            QemuPromptAgentTests.Tool("live_desktop", new { }, "screen"),
            QemuPromptAgentTests.Tool("finish", new { success = true, summary = "Opened the requested page and verified its title." }, "done"));
        var config = QemuPromptAgentTests.OptionsForAgent();
        config.Enabled = true;
        config.UseKvm = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_KVM") == "true";
        config.Images = new() { ["browser"] = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_IMAGE")! };
        var options = Options.Create(config);
        var runner = new QemuEnvironmentRunner(options, new QemuPromptAgent(model, options));
        var result = await runner.RunAsync(new("browser", "Open the local test page in Chromium and verify its title.", memoryMb: 2048, cpus: 2, timeoutSeconds: 180), "{}", default);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Contains("verified its title", result.Stdout);
        Assert.Equal(3, result.AgentSteps);
        Assert.Equal(2, result.Activity!.Count);
        Assert.Contains("AGENT_BROWSER_VERIFIED", model.Requests[1].ToJsonString());
        Assert.Contains("data:image/png;base64,", model.Requests[2].ToJsonString());
    }
}
