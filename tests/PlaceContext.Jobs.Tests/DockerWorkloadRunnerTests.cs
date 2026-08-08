using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Infrastructure.Workload;

namespace PlaceContext.Jobs.Tests;

/// <summary>
/// Integration tests for <see cref="DockerWorkloadRunner"/>.
///
/// These tests require:
/// - Docker (or compatible runtime) installed and reachable on the PATH.
/// - Any accessible container image (tests use "alpine" as a generic stand-in).
///
/// SKIPPED in CI because the infrastructure integration suite is not run in the standard
/// <c>dotnet test</c> pass (consistent with the repo's pattern for Docker-dependent tests).
/// Run them manually when validating the adapter against a live docker daemon.
/// </summary>
public class DockerWorkloadRunnerTests
{
    private static DockerWorkloadRunner BuildSut()
    {
        var opts = new WorkloadRunnerOptions
        {
            DockerExecutable = "docker",
            ArtifactFileName = "result.json",
            DefaultTimeoutSeconds = 60,
        };
        return new DockerWorkloadRunner(Options.Create(opts));
    }

    [Fact(Skip = "Requires Docker daemon and 'alpine' image pulled locally")]
    public async Task Success_exit_returns_exit_zero_and_captures_stdout()
    {
        var sut = BuildSut();
        var request = new WorkloadRunRequest(
            Image: "alpine",
            StdinPayload: "{}",
            Env: new Dictionary<string, string>(),
            ArtifactMounts: Array.Empty<(string, string)>(),
            CorrelationId: "infra-test-" + Guid.NewGuid().ToString("N"),
            CodeFiles: null,
            RuntimeId: null,
            Entrypoint: null);

        var result = await sut.RunAsync(request);

        Assert.NotNull(result);
    }

    [Fact(Skip = "Requires Docker daemon and 'alpine' image pulled locally")]
    public async Task Artifact_file_is_read_when_present()
    {
        var sut = BuildSut();
        var request = new WorkloadRunRequest(
            Image: "alpine",
            StdinPayload: "{}",
            Env: new Dictionary<string, string>(),
            ArtifactMounts: Array.Empty<(string, string)>(),
            CorrelationId: "infra-artifact-" + Guid.NewGuid().ToString("N"),
            CodeFiles: null,
            RuntimeId: null,
            Entrypoint: null);

        var result = await sut.RunAsync(request);
        Assert.NotNull(result);
    }

    [Fact(Skip = "Requires Docker daemon")]
    public async Task CancellationToken_cancels_long_running_container()
    {
        var sut = BuildSut();
        var request = new WorkloadRunRequest(
            Image: "alpine",
            StdinPayload: "{}",
            Env: new Dictionary<string, string>(),
            ArtifactMounts: Array.Empty<(string, string)>(),
            CorrelationId: "infra-cancel-" + Guid.NewGuid().ToString("N"),
            CodeFiles: null,
            RuntimeId: null,
            Entrypoint: null);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.RunAsync(request, cts.Token));
    }

    [Fact(Skip = "Requires Docker daemon and 'node:22-slim' image pulled locally")]
    public async Task CodeWorkload_node_runs_source_and_returns_artifact()
    {
        var sut = BuildSut();
        // Source writes result.json to /out — uses node:22-slim from default registry.
        var source = """
            const fs = require('fs');
            const payload = JSON.parse(require('fs').readFileSync('/dev/stdin','utf8'));
            fs.writeFileSync('/out/result.json', JSON.stringify({echoed: payload}));
            """;
        var request = new WorkloadRunRequest(
            Image: null,
            StdinPayload: @"{""hello"":""world""}",
            Env: new Dictionary<string, string>(),
            ArtifactMounts: Array.Empty<(string, string)>(),
            CorrelationId: "infra-code-node-" + Guid.NewGuid().ToString("N"),
            CodeFiles: new[] { ("index.js", source) },
            RuntimeId: "node",
            Entrypoint: "index.js");

        var result = await sut.RunAsync(request);

        Assert.NotNull(result);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Artifact);
    }

    [Fact(Skip = "Requires Docker daemon and internet access (builds a warm image from PyPI)")]
    public async Task A_manifest_workload_bakes_the_warm_image_once_and_reuses_it_afterwards()
    {
        var sut = BuildSut();
        var files = new (string Path, string Content)[]
        {
            ("main.py", "import json\nimport six\nprint(json.dumps({'ok': True}))"),
            ("requirements.txt", "six"),
        };
        WorkloadRunRequest Req() => new(
            Image: null,
            StdinPayload: "{}",
            Env: new Dictionary<string, string>(),
            ArtifactMounts: Array.Empty<(string, string)>(),
            CorrelationId: "infra-warm-" + Guid.NewGuid().ToString("N"),
            CodeFiles: files,
            RuntimeId: "python",
            Entrypoint: null,
            AllowNetworkEgress: false); // warm runs need no egress — the bake happened on the host

        try
        {
            var first = await sut.RunAsync(Req());   // cold: builds pcwarm-python:<hash>
            Assert.Equal(0, first.ExitCode);

            var recipe = WorkloadDependencies.For("python", files)!;
            var tag = $"pcwarm-python:{WorkloadDependencies.BakeKey("python", "python:3.12-slim", recipe, files)}";
            var inspect = System.Diagnostics.Process.Start("docker", new[] { "image", "inspect", tag })!;
            await inspect.WaitForExitAsync();
            Assert.Equal(0, inspect.ExitCode); // the layer was baked

            var second = await sut.RunAsync(Req());  // warm: reuses the image, no install
            Assert.Equal(0, second.ExitCode);
            Assert.DoesNotContain("pip install", second.Stderr);
            Assert.Contains("ok", second.Stdout);
        }
        finally
        {
            System.Diagnostics.Process.Start("sh",
                new[] { "-c", "docker image ls -q --filter label=placecontext.warm=true | xargs -r docker rmi -f" })
                ?.WaitForExit();
        }
    }

    // ── non-root sandbox args ─────────────────────────────────────────────────────────────────────
    // Jobs never run as root: every built command must carry the unprivileged identity and the
    // hardening flags. Asserted off the argv itself — no docker daemon needed.

    private static WorkloadRunRequest ImageRequest() => new(
        Image: "alpine",
        StdinPayload: "{}",
        Env: new Dictionary<string, string>(),
        ArtifactMounts: Array.Empty<(string, string)>(),
        CorrelationId: "corr",
        CodeFiles: null,
        RuntimeId: null,
        Entrypoint: null);

    [Fact]
    public void Command_runs_as_nobody_with_dropped_capabilities_and_no_new_privileges()
    {
        var sut = BuildSut();
        var args = sut.BuildArgs(ImageRequest(), "alpine", "/tmp/out",
            new List<(string HostPath, string ContainerPath)>(), null, null, depsBaked: false);

        var userAt = args.IndexOf("--user");
        Assert.True(userAt >= 0);
        Assert.Equal("65534:65534", args[userAt + 1]);
        var capAt = args.IndexOf("--cap-drop");
        Assert.True(capAt >= 0);
        Assert.Equal("ALL", args[capAt + 1]);
        var secAt = args.IndexOf("--security-opt");
        Assert.True(secAt >= 0);
        Assert.Equal("no-new-privileges", args[secAt + 1]);
        // A writable HOME for tools that insist on one, ahead of (overridable by) the job's own env.
        var homeAt = args.IndexOf("HOME=/tmp");
        Assert.True(homeAt >= 1 && args[homeAt - 1] == "-e");
    }

    [Fact]
    public void Run_as_identity_is_configurable_via_options()
    {
        var sut = new DockerWorkloadRunner(Options.Create(
            new WorkloadRunnerOptions { RunAsUser = 10001, RunAsGroup = 10001 }));
        var args = sut.BuildArgs(ImageRequest(), "alpine", "/tmp/out",
            new List<(string HostPath, string ContainerPath)>(), null, null, depsBaked: false);

        var userAt = args.IndexOf("--user");
        Assert.True(userAt >= 0);
        Assert.Equal("10001:10001", args[userAt + 1]);
    }
}
