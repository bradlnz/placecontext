using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Workload;
using PlaceContext.Application.Build;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Workload;

// shortcut: images stay on this execution host; use a registry before distributing these steps across hosts.
public sealed class ContainerWorkflowBackgroundService(IOptions<ContainerWorkflowOptions> options, IOptions<WorkloadRunnerOptions> workloads,
    ILogger<ContainerWorkflowBackgroundService> log, IOutcomeReviewer? reviewer = null, IExecutionApprovals? approvals = null) : BackgroundService, IContainerWorkflowRunner
{
    private const string RunLabel = "placecontext.workflow.run";
    private const string ExpiryLabel = "placecontext.workflow.expires";
    private readonly SemaphoreSlim _buildCapacity = new(1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _activeRuns = new();
    private readonly string _clientConfig = Path.Combine(Path.GetTempPath(), "pcdocker-" + Guid.NewGuid().ToString("N"));
    public IAsyncDisposable TrackRun(Guid runId) => new RunLease(this, runId);
    private sealed class RunLease(ContainerWorkflowBackgroundService runner, Guid id) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(runner.CleanupAsync(id));
    }

    public async Task<ContainerStepResult> ExecuteAsync(ChainAction action, Guid projectId, Guid runId, int step, string? payload, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            throw new InvalidOperationException("Container workflow execution is disabled. Enable PlaceContext:ContainerWorkflows:Enabled on an execution host with Docker.");
        }

        _activeRuns.TryAdd(runId, 0);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(action is ContainerBuildChainAction b ? b.TimeoutSeconds : ((ContainerRunChainAction)action).TimeoutSeconds));
        try
        {
            return action switch
            {
                ContainerBuildChainAction build => await BuildAsync(build, projectId, runId, step, payload, deadline.Token),
                ContainerRunChainAction run => await RunAsync(run, projectId, runId, step, payload, deadline.Token),
                _ => throw new ArgumentException("Unsupported container action.")
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(-1, "", "Container step exceeded its timeout.");
        }
    }

    internal static JsonNode? Field(string? payload, string path)
    {
        if (Encoding.UTF8.GetByteCount(payload ?? "") > 4 * 1024 * 1024)
        {
            throw new ArgumentException("Container input exceeds 4 MiB.");
        }

        JsonNode? node = string.IsNullOrWhiteSpace(payload) ? null : JsonNode.Parse(payload);
        foreach (var part in path.Split('.'))
        {
            node = (node as JsonObject)?[part];
        }

        return node;
    }

    internal static IReadOnlyDictionary<string, string> BuildFiles(string? payload, string field)
    {
        var node = Field(payload, field);
        if (node is null)
        {
            return new Dictionary<string, string>();
        }

        if (node is not JsonObject files || files.Count > 128)
        {
            throw new ArgumentException("Build context must be an object of up to 128 relative file paths and text contents.");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, content) in files)
        {
            if (path.Length is < 1 or > 240 || path.Contains('\\') || path.Any(char.IsControl)
                || path.Split('/').Any(segment => segment is "" or "." or "..") || Path.IsPathRooted(path)
                || path is "Dockerfile" or ".dockerignore" || content is not JsonValue value || !value.TryGetValue<string>(out var text))
            {
                throw new ArgumentException("Build context needs safe relative file paths and text contents; Dockerfile and .dockerignore are reserved.");
            }

            result.Add(path, text);
        }
        return result;
    }

    private async Task<ContainerStepResult> BuildAsync(ContainerBuildChainAction action, Guid project, Guid run, int step, string? payload, CancellationToken ct)
    {
        var files = BuildFiles(payload, action.ContextField);
        await ExecutionSafetyReview.RequireAsync(reviewer, new
        {
            kind = "container build", action.Dockerfile, files, action.AllowNetwork, action.TimeoutSeconds,
            sandbox = "Isolated BuildKit builder with memory and CPU limits; no host mounts."
        }, project, ct, approvals, run);
        var dir = Path.Combine(Path.GetTempPath(), "pcbuild-" + Guid.NewGuid().ToString("N"));
        var image = $"pcwf-{project:N}-{run:N}-{step}:latest";
        var builder = $"pcwf-build-{run:N}-{step}";
        await _buildCapacity.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(dir);
            foreach (var (path, content) in files)
            {
                var target = Path.Combine(dir, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllTextAsync(target, content, ct);
            }
            await File.WriteAllTextAsync(Path.Combine(dir, "Dockerfile"), action.Dockerfile, ct);
            var create = await DockerAsync(["buildx", "create", "--name", builder, "--driver", "docker-container", "--driver-opt", "memory=2g,cpu-quota=200000"], ct);
            if (create.ExitCode != 0)
            {
                return create;
            }

            var result = await DockerAsync(["buildx", "build", "--builder", builder, "--load", "--pull", "--network", action.AllowNetwork ? "default" : "none", "--label", ExpiryLabel + "=" + DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(), "--tag", image, dir], ct);
            return result with
            {
                Image = result.ExitCode == 0 ? image : null
            };
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                await DockerAsync(["buildx", "rm", "--force", builder], cleanup.Token);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Could not remove workflow image builder {Builder}", builder);
            }
            _buildCapacity.Release();
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException ex)
            {
                log.LogWarning(ex, "Could not remove container build context");
            }
        }
    }

    private async Task<ContainerStepResult> RunAsync(ContainerRunChainAction action, Guid project, Guid run, int step, string? payload, CancellationToken ct)
    {
        var image = action.Image.Length > 0 ? action.Image : Field(payload, action.ImageField)?.GetValue<string>() ?? "";
        ContainerRunChainAction.ValidateImage(image);
        if (image.StartsWith("pcwf-", StringComparison.Ordinal))
        {
            if (!image.StartsWith($"pcwf-{project:N}-", StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("Built image belongs to another project.");
            }
        }
        if (!image.StartsWith("pcwf-", StringComparison.Ordinal))
        {
            if (image.StartsWith("sha256:", StringComparison.Ordinal) || image.All(char.IsAsciiHexDigit))
            {
                throw new ArgumentException("Use a registry image reference, not a local image ID.");
            }
        }
        await ExecutionSafetyReview.RequireAsync(reviewer, new
        {
            kind = "container run", image, action.Command, input = payload,
            action.Port, action.AllowNetwork, action.MemoryMb, action.Cpus, action.TimeoutSeconds,
            sandbox = "Unprivileged read-only container; no host mounts; capabilities dropped; process limit enforced."
        }, project, ct, approvals, run);
        if (!image.StartsWith("pcwf-", StringComparison.Ordinal))
        {
            var pull = await DockerAsync(["pull", image], ct);
            if (pull.ExitCode != 0)
            {
                return pull;
            }
        }
        var name = $"pcwf-{run:N}-{step}";
        await RemoveContainerAsync(name);
        var network = "none";
        if (action.AllowNetwork)
        {
            network = "bridge";
        }

        if (action.Port is not null)
        {
            network = name + "-net";
            await DockerAsync(["network", "rm", network], ct);
            var networkArgs = new List<string> { "network", "create", "--label", RunLabel + "=" + run.ToString("N"), "--label", ExpiryLabel + "=" + DateTimeOffset.UtcNow.AddSeconds(action.TimeoutSeconds).ToUnixTimeSeconds() };
            if (!action.AllowNetwork)
            {
                networkArgs.Add("--internal");
            }

            networkArgs.Add(network);
            var create = await DockerAsync(networkArgs, ct);
            if (create.ExitCode != 0)
            {
                return create;
            }
        }
        var keep = false;
        try
        {
            var result = await DockerAsync(RunArguments(action, image, name, network, run), ct, action.Port is null ? payload ?? "{}" : null);
            if (result.ExitCode != 0 || action.Port is null)
            {
                return result with
                {
                    Image = image
                };
            }

            var address = await DockerAsync(["inspect", "--format", "{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}", name], ct);
            if (address.ExitCode != 0 || !System.Net.IPAddress.TryParse(address.Stdout.Trim(), out var ip))
            {
                return new(-1, result.Stdout, "Container did not receive a private network address.");
            }

            var url = $"http://{ip}:{action.Port.Value}";
            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
            while (true)
            {
                var state = await DockerAsync(["inspect", "--format", "{{.State.Running}}", name], ct);
                if (state.Stdout.Trim() != "true")
                {
                    var logs = await DockerAsync(["logs", "--tail", "100", name], ct);
                    return new(-1, logs.Stdout, "Web container exited before it was ready. " + logs.Stderr, image);
                }
                try
                {
                    using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    if ((int)response.StatusCode < 500)
                    {
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                    
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    
                }
                await Task.Delay(250, ct);
            }
            keep = true;
            return new(0, "Web container is ready; it will stop when this workflow exits or its lifetime expires.", "", image, url, url);
        }
        finally
        {
            if (!keep)
            {
                await RemoveContainerAsync(name);
            }
        }
    }

    internal static List<string> RunArguments(ContainerRunChainAction action, string image, string name, string network, Guid run)
    {
        var args = new List<string> { "run", action.Port is null ? "-i" : "-d", "--name", name,
            "--label", RunLabel + "=" + run.ToString("N"), "--label", ExpiryLabel + "=" + DateTimeOffset.UtcNow.AddSeconds(action.TimeoutSeconds).ToUnixTimeSeconds(),
            "--user", "65534:65534", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
            "--read-only", "--tmpfs", "/tmp:rw,nosuid,nodev,noexec,size=64m", "--pids-limit", "128",
            "--memory", action.MemoryMb.ToString(CultureInfo.InvariantCulture) + "m", "--cpus", action.Cpus.ToString(CultureInfo.InvariantCulture),
            "--network", network, "--env", "HOME=/tmp",
            "--log-driver", "local", "--log-opt", "max-size=1m", "--log-opt", "max-file=2" };
        args.Add(image);
        args.AddRange(action.Command);
        return args;
    }

    private async Task<ContainerStepResult> DockerAsync(IEnumerable<string> args, CancellationToken ct, string? stdin = null)
    {
        var start = new ProcessStartInfo(workloads.Value.DockerExecutable) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
        Directory.CreateDirectory(_clientConfig);
        // Use anonymous registry access; never inherit host registry credentials.
        start.Environment["DOCKER_CONFIG"] = _clientConfig;
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Docker.");
        var stdout = CaptureAsync(process.StandardOutput, ct);
        var stderr = CaptureAsync(process.StandardError, ct);
        try
        {
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin.AsMemory(), ct);
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync(ct);
            return new(process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            try
            {
                await Task.WhenAll(stdout, stderr);
            }
            catch (OperationCanceledException)
            {
                
            }
        }
    }
    private static async Task<string> CaptureAsync(StreamReader reader, CancellationToken ct)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (output.Length < 65536)
            {
                output.Append(buffer, 0, Math.Min(count, 65536 - output.Length));
            }
        }

        return output.ToString();
    }
    private async Task RemoveContainerAsync(string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await DockerAsync(["rm", "--force", name], timeout.Token);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not remove workflow container {Name}", name);
        }
    }
    private async Task CleanupAsync(Guid run)
    {
        if (!options.Value.Enabled || !_activeRuns.TryRemove(run, out _))
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var containers = await DockerAsync(["ps", "-aq", "--filter", "label=" + RunLabel + "=" + run.ToString("N")], timeout.Token);
            foreach (var id in containers.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                await RemoveContainerAsync(id.Trim());
            }

            var networks = await DockerAsync(["network", "ls", "-q", "--filter", "label=" + RunLabel + "=" + run.ToString("N")], timeout.Token);
            foreach (var id in networks.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                await DockerAsync(["network", "rm", id.Trim()], timeout.Token);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Workflow container cleanup failed for {Run}", run);
        }
    }
    public override void Dispose()
    {
        base.Dispose();
        try
        {
            if (Directory.Exists(_clientConfig))
            {
                Directory.Delete(_clientConfig, recursive: true);
            }
        }
        catch (IOException ex)
        {
            log.LogWarning(ex, "Could not remove workflow Docker client configuration");
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                var builders = await DockerAsync(["ps", "-aq", "--filter", "name=buildx_buildkit_pcwf-build-"], deadline.Token);
                foreach (var id in builders.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var created = await DockerAsync(["inspect", "--format", "{{.Created}}", id.Trim()], deadline.Token);
                    if (DateTimeOffset.TryParse(created.Stdout.Trim(), out var started) && started < DateTimeOffset.UtcNow.AddHours(-1))
                    {
                        var mounts = await DockerAsync(["inspect", "--format", "{{range .Mounts}}{{if eq .Type \"volume\"}}{{.Name}}\n{{end}}{{end}}", id.Trim()], deadline.Token);
                        await DockerAsync(["rm", "--force", id.Trim()], deadline.Token);
                        foreach (var volume in mounts.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(v => v.StartsWith("buildx_buildkit_pcwf-build-", StringComparison.Ordinal)))
                        {
                            await DockerAsync(["volume", "rm", volume.Trim()], deadline.Token);
                        }
                    }
                }
                foreach (var kind in new[] { "container", "network", "image" })
                {
                    var list = await DockerAsync([kind, "ls", kind == "network" ? "-q" : "-aq", "--filter", "label=" + ExpiryLabel], deadline.Token);
                    foreach (var id in list.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Distinct())
                    {
                        if (kind == "image")
                        {
                            var tags = await DockerAsync(["image", "inspect", "--format", "{{range .RepoTags}}{{println .}}{{end}}", id.Trim()], deadline.Token);
                            if (!tags.Stdout.Split('\n').Any(tag => tag.StartsWith("pcwf-", StringComparison.Ordinal)))
                            {
                                continue;
                            }
                        }
                        var format = kind == "network" ? "{{index .Labels \"" + ExpiryLabel + "\"}}" : "{{index .Config.Labels \"" + ExpiryLabel + "\"}}";
                        var expiry = await DockerAsync([kind, "inspect", "--format", format, id.Trim()], deadline.Token);
                        if (long.TryParse(expiry.Stdout.Trim(), out var expires) && expires < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                        {
                            await DockerAsync(kind == "container" ? [kind, "rm", "--force", id.Trim()] : [kind, "rm", id.Trim()], deadline.Token);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Expired workflow sandbox cleanup failed");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
