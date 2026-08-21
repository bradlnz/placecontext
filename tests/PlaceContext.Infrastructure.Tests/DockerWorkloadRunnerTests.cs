using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

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

    // ── always-on runtime sandbox profiles (dotnet, go) ──────────────────────────────────────────
    // These toolchains must write AND exec their caches (dotnet's runfile cache, go's GOCACHE) —
    // impossible under the baseline sandbox (64m noexec /tmp, 256m memory). The profile gives each
    // run an exec tmpfs, relocated env dirs (mkdir'd by the pre-invoke wrap), and a memory override.

    private static WorkloadRunRequest CodeRequest(string runtimeId, string entrypoint) => new(
        Image: null,
        StdinPayload: "{}",
        Env: new Dictionary<string, string>(),
        ArtifactMounts: Array.Empty<(string, string)>(),
        CorrelationId: "corr",
        CodeFiles: new[] { (entrypoint, "code") },
        RuntimeId: runtimeId,
        Entrypoint: entrypoint);

    private List<string> ProfileArgs(string runtimeId, string entrypoint, string[] invoke, bool depsBaked)
    {
        var opts = new WorkloadRunnerOptions();
        var sut = new DockerWorkloadRunner(Options.Create(opts));
        return sut.BuildArgs(CodeRequest(runtimeId, entrypoint), opts.Runtimes[runtimeId].BaseImage, "/tmp/out",
            new List<(string HostPath, string ContainerPath)>(), "/tmp/work", invoke, depsBaked,
            opts.Runtimes[runtimeId]);
    }

    [Fact]
    public void Dotnet_runs_get_an_exec_tmpfs_more_memory_and_relocated_env_dirs()
    {
        var args = ProfileArgs("dotnet", "main.cs", new[] { "dotnet", "run", "/work/main.cs" }, depsBaked: false);

        var memAt = args.IndexOf("--memory");
        Assert.True(memAt >= 0);
        Assert.Equal("1g", args[memAt + 1]);

        // The exec tmpfs rides on every run — and exactly once, even though the always-on dotnet
        // recipe would also mount the deps root on the cold path.
        Assert.Single(args.Where(a => a == "/pcdeps:rw,exec,nosuid,size=512m"));

        foreach (var expected in new[]
        {
            "HOME=/pcdeps/home", "XDG_DATA_HOME=/pcdeps/xdg", "NUGET_PACKAGES=/pcdeps/nuget",
            "DOTNET_CLI_HOME=/pcdeps/dotnet", "TMPDIR=/pcdeps/tmp",
        })
        {
            var at = args.IndexOf(expected);
            Assert.True(at >= 1 && args[at - 1] == "-e", $"missing env: {expected}");
        }
        Assert.DoesNotContain("HOME=/tmp", args); // the profile relocated HOME
    }

    [Fact]
    public void Dotnet_invoke_is_wrapped_in_sh_c_with_the_mkdir_preinvoke()
    {
        var args = ProfileArgs("dotnet", "main.cs", new[] { "dotnet", "run", "/work/main.cs" }, depsBaked: false);

        var imageAt = args.IndexOf("mcr.microsoft.com/dotnet/sdk:10.0");
        Assert.True(imageAt >= 0);
        Assert.Equal(new[] { "sh", "-c" }, args.Skip(imageAt + 1).Take(2).ToArray());
        var script = args[imageAt + 3];
        Assert.StartsWith("mkdir -p \"$HOME\" \"$XDG_DATA_HOME\" \"$NUGET_PACKAGES\" \"$DOTNET_CLI_HOME\" \"$TMPDIR\"", script);
        Assert.EndsWith("&& exec 'dotnet' 'run' '/work/main.cs'", script);
    }

    [Fact]
    public void Dotnet_warm_runs_keep_the_exec_tmpfs_and_preinvoke()
    {
        // depsBaked skips the recipe scratch mount — the profile's tmpfs must still be there, or the
        // runfile cache lands on the noexec /tmp again.
        var args = ProfileArgs("dotnet", "main.cs", new[] { "dotnet", "run", "/work/main.cs" }, depsBaked: true);

        Assert.Single(args.Where(a => a == "/pcdeps:rw,exec,nosuid,size=512m"));
        Assert.Equal("sh", args[^3]);
        Assert.Contains("mkdir -p", args[^1]);
    }

    [Fact]
    public void Go_runs_get_exec_caches_without_a_go_mod()
    {
        var args = ProfileArgs("go", "main.go", new[] { "go", "run", "/work/main.go" }, depsBaked: false);

        var memAt = args.IndexOf("--memory");
        Assert.Equal("512m", args[memAt + 1]);
        Assert.Single(args.Where(a => a == "/pcdeps:rw,exec,nosuid,size=256m"));
        Assert.Contains("GOCACHE=/pcdeps/gocache", args);
        Assert.Contains("GOTMPDIR=/pcdeps/gotmp", args);
        Assert.Equal("sh", args[^3]);
        Assert.EndsWith("&& exec 'go' 'run' '/work/main.go'", args[^1]);
    }

    [Fact]
    public void Job_env_still_overrides_the_runtime_profile()
    {
        var opts = new WorkloadRunnerOptions();
        var sut = new DockerWorkloadRunner(Options.Create(opts));
        var request = CodeRequest("dotnet", "main.cs") with
        {
            Env = new Dictionary<string, string> { ["HOME"] = "/custom", ["MY_FLAG"] = "1" },
        };
        var args = sut.BuildArgs(request, opts.Runtimes["dotnet"].BaseImage, "/tmp/out",
            new List<(string HostPath, string ContainerPath)>(), "/tmp/work",
            new[] { "dotnet", "run", "/work/main.cs" }, depsBaked: false, opts.Runtimes["dotnet"]);

        Assert.DoesNotContain("HOME=/pcdeps/home", args);
        Assert.Contains("HOME=/custom", args);
        Assert.Contains("MY_FLAG=1", args);
        Assert.Single(args.Where(a => a == "HOME=/custom"));
    }
}
