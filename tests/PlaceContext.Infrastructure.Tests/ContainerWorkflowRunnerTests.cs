using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.Application.Workload.VirtualMachines;
namespace PlaceContext.Infrastructure.Tests;

public sealed class ContainerWorkflowRunnerTests
{
    [Theory]
    [InlineData("../escape")]
    [InlineData("/tmp/escape")]
    [InlineData("a/../../escape")]
    [InlineData("a\\escape")]
    [InlineData("Dockerfile")]
    [InlineData(".dockerignore")]
    public void Build_context_rejects_unsafe_paths(string path) => Assert.Throws<ArgumentException>(() =>
        ContainerWorkflowBackgroundService.BuildFiles(JsonSerializer.Serialize(new
        {
            files = new Dictionary<string, string> { [path] = "bad" }
        }), "files"));

    [Fact]
    public void Context_files_require_strings_and_enforce_size_and_count_limits()
    {
        Assert.Equal("Hello", ContainerWorkflowBackgroundService.BuildFiles("""{"source":{"files":{"web/index.html":"Hello"}}}""", "source.files")["web/index.html"]);
        Assert.Throws<ArgumentException>(() => ContainerWorkflowBackgroundService.BuildFiles("""{"files":{"index":42}}""", "files"));
        Assert.Throws<ArgumentException>(() => ContainerWorkflowBackgroundService.BuildFiles(new string(' ', 4 * 1024 * 1024 + 1), "files"));
        Assert.Throws<ArgumentException>(() => ContainerWorkflowBackgroundService.BuildFiles(JsonSerializer.Serialize(new { files = Enumerable.Range(0, 129).ToDictionary(i => i.ToString(), _ => "x") }), "files"));
    }

    [Fact]
    public void Sandbox_cannot_gain_privileges_or_publish_a_public_port()
    {
        var args = ContainerWorkflowBackgroundService.RunArguments(new(port: 8080), "image", "name", "internal", Guid.NewGuid());
        foreach (var protection in new[] { "65534:65534", "ALL", "no-new-privileges", "--read-only", "--pids-limit", "--memory", "--cpus", "internal", "--log-driver", "local", "max-size=1m", "max-file=2" })
        {
            Assert.Contains(protection, args);
        }

        Assert.DoesNotContain("--publish", args);
        Assert.DoesNotContain("--privileged", args);
        Assert.DoesNotContain("-v", args);
        Assert.Throws<ArgumentException>(() => new ContainerRunChainAction("--privileged", ""));
        Assert.Throws<ArgumentException>(() => new ContainerRunChainAction("alpine", "built.image"));
        Assert.Throws<ArgumentException>(() => new ContainerRunChainAction(port: 80));
    }

    [ContainerDockerFact]
    public async Task Build_command_web_readiness_cancellation_and_cleanup_work_on_docker()
    {
        using var runner = new ContainerWorkflowBackgroundService(Options.Create(new ContainerWorkflowOptions { Enabled = true }),
            Options.Create(new WorkloadRunnerOptions()), NullLogger<ContainerWorkflowBackgroundService>.Instance);
        var project = Guid.NewGuid();
        var run = Guid.NewGuid();
        string? url = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using (runner.TrackRun(run))
        {
            var build = await runner.ExecuteAsync(new ContainerBuildChainAction("FROM python:3.13-alpine\nWORKDIR /app\nCOPY . .\nUSER 65534:65534\nCMD [\"python\",\"-m\",\"http.server\",\"8080\"]"), project, run, 0,
                """{"files":{"index.html":"Workflow sandbox smoke"}}""", timeout.Token);
            Assert.True(build.ExitCode == 0, build.Stderr);
            Assert.NotNull(build.Image);
            var payload = JsonSerializer.Serialize(new
            {
                container_build = new
                {
                    image = build.Image
                }
            });
            var command = await runner.ExecuteAsync(new ContainerRunChainAction(command: ["python", "-c", "import os; assert os.getuid()==65534; print('sandbox-ok')"]), project, run, 1, payload, timeout.Token);
            Assert.Equal(0, command.ExitCode);
            Assert.Contains("sandbox-ok", command.Stdout);
            var failed = await runner.ExecuteAsync(new ContainerRunChainAction(command: ["python", "-c", "open('/forbidden','w')"]), project, run, 2, payload, timeout.Token);
            Assert.NotEqual(0, failed.ExitCode);
            var web = await runner.ExecuteAsync(new ContainerRunChainAction(port: 8080, timeoutSeconds: 300), project, run, 3, payload, timeout.Token);
            Assert.True(web.ExitCode == 0, web.Stderr);
            Assert.NotNull(web.Url);
            Assert.Equal(web.Url, web.QemuUrl);
            url = web.Url;
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            Assert.Contains("Workflow sandbox smoke", await http.GetStringAsync(url, timeout.Token));
            if (Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_IMAGE") is { Length: > 0 } guestImage)
            {
                var qemu = new VirtualMachineEnvironmentRunner(Options.Create(new VirtualMachineEnvironmentOptions
                {
                    Images = new() { ["browser"] = guestImage },
                    UseKvm = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_KVM") == "true"
                }), new HttpGuest());
                var verified = await qemu.RunAsync(new VirtualMachineChainAction("browser", "Read sandbox page", memoryMb: 2048, timeoutSeconds: 180, allowNetwork: true), web.QemuUrl, timeout.Token);
                Assert.True(verified.ExitCode == 0, verified.Stderr);
                Assert.Contains("Workflow sandbox smoke", verified.Stdout);
            }
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.ExecuteAsync(new ContainerRunChainAction(command: ["sleep", "60"]), project, run, 4, payload, cancel.Token));
            var timed = await runner.ExecuteAsync(new ContainerRunChainAction(command: ["sleep", "60"], timeoutSeconds: 10), project, run, 5, payload, timeout.Token);
            Assert.Equal(-1, timed.ExitCode);
            Assert.Contains("timeout", timed.Stderr);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => runner.ExecuteAsync(new ContainerRunChainAction(), Guid.NewGuid(), run, 6, payload, timeout.Token));
            var pulled = await runner.ExecuteAsync(new ContainerRunChainAction("python:3.13-alpine", "", command: ["python", "-c", "print('pulled-ok')"]), project, run, 7, null, timeout.Token);
            Assert.Equal(0, pulled.ExitCode);
            Assert.Contains("pulled-ok", pulled.Stdout);
        }
        foreach (var kind in new[] { "container", "network" })
        {
            var start = new System.Diagnostics.ProcessStartInfo("docker") { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var arg in new[] { kind, "ls", kind == "container" ? "-aq" : "-q", "--filter", "label=placecontext.workflow.run=" + run.ToString("N") })
            {
                start.ArgumentList.Add(arg);
            }

            using var check = System.Diagnostics.Process.Start(start)!;
            var remaining = await check.StandardOutput.ReadToEndAsync(timeout.Token);
            await check.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, check.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(remaining), "Workflow left " + kind + " resources running: " + remaining);
        }
    }
    private sealed class HttpGuest : IVirtualMachineTaskAgent
    {
        public string? ConfigurationError => null;
        public string Model => "HTTP integration check";
        public Task<VirtualMachineEnvironmentResult> RunAsync(string prompt, string? input,
            Func<string, int, CancellationToken, Task<VirtualMachineEnvironmentResult>> executeGuest, CancellationToken ct,
            Func<VirtualMachineReplayCapture, CancellationToken, Task>? recordFrame = null, VirtualMachineAgentInteraction? interaction = null,
            Func<CancellationToken, Task<byte[]>>? readLiveFrame = null)
        {
            var script = "import urllib.request; print(urllib.request.urlopen(" + JsonSerializer.Serialize(input) + ",timeout=10).read().decode())";
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(script));
            return executeGuest("printf '%s' '" + encoded + "' | base64 -d | python3", 8192, ct);
        }
    }

}
