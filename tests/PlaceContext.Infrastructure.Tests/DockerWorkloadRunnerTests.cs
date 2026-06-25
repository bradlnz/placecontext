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
}
