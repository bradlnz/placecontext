using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Workload;

/// <summary>Routes explicitly registered native images to persistent workers; other workloads retain their sandbox runner.</summary>
public sealed class PersistentWorkloadRunner(
    IOptions<WorkloadRunnerOptions> options,
    IHttpClientFactory clients,
    IWorkloadRunner fallback) : IWorkloadRunner
{
    private readonly WorkloadRunnerOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, string> _active = new();
    private int _next;

    public async Task<WorkloadRunResult> RunAsync(WorkloadRunRequest request, CancellationToken ct = default)
    {
        var inlineRust = request.CodeFiles is not null && request.RuntimeId == "rust";
        if (!inlineRust && (request.Image is null || !_options.PersistentRunnerImages.Contains(request.Image, StringComparer.Ordinal)))
            return await fallback.RunAsync(request, ct);

        if (request.Network is not null)
            throw new InvalidOperationException("Persistent workers cannot enforce a per-job VPC. Use a sandboxed Kubernetes workload for this job.");

        if ((!inlineRust && request.CodeFiles is not null) || request.ArtifactMounts.Count != 0)
            throw new InvalidOperationException("Persistent Rust jobs require inline Rust source or a registered migration image, without reduce mounts.");
        if (!request.Env.TryGetValue("OSSEN_JOB", out var stage) || string.IsNullOrWhiteSpace(stage))
            throw new InvalidOperationException("Persistent native jobs require OSSEN_JOB.");
        if (_options.PersistentRunnerUrls.Length == 0 || _options.PersistentRunnerToken.Length < 32)
            throw new InvalidOperationException("Persistent native runner endpoints and credentials are required.");

        var seconds = request.TimeoutSeconds is > 0 ? request.TimeoutSeconds.Value : _options.DefaultTimeoutSeconds;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds + 5));
        var client = clients.CreateClient("persistent-workloads");
        var first = (uint)Interlocked.Increment(ref _next) % (uint)_options.PersistentRunnerUrls.Length;
        try
        {
            while (true)
            {
                for (var offset = 0; offset < _options.PersistentRunnerUrls.Length; offset++)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    var endpoint = _options.PersistentRunnerUrls[(first + offset) % _options.PersistentRunnerUrls.Length].TrimEnd('/');
                    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                        throw new InvalidOperationException("Invalid persistent runner endpoint.");
                    using var message = new HttpRequestMessage(HttpMethod.Post, endpoint + "/run");
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.PersistentRunnerToken);
                    message.Content = JsonContent.Create(new
                    {
                        request.CorrelationId,
                        Stage = stage,
                        request.StdinPayload,
                        request.Env,
                        TimeoutSeconds = seconds,
                        request.AllowNetworkEgress,
                        CodeFiles = request.CodeFiles?.Select(file => new { file.Path, file.Content }).ToArray(),
                        request.Entrypoint
                    });
                    _active[request.CorrelationId] = endpoint;
                    using var response = await client.SendAsync(message, deadline.Token);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        _active.TryRemove(request.CorrelationId, out _);
                        continue;
                    }
                    // No fallback or retry after acceptance: that could duplicate provider or delivery side effects.
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadFromJsonAsync<WorkloadRunResult>(deadline.Token)
                        ?? throw new InvalidOperationException("Persistent runner returned no result.");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
            }
        }
        catch (OperationCanceledException)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await CancelAsync(request.CorrelationId, cleanup.Token);
            throw;
        }
        finally
        {
            _active.TryRemove(request.CorrelationId, out _);
        }
    }

    public async Task CancelAsync(string correlationId, CancellationToken ct = default)
    {
        var endpoints = _active.TryGetValue(correlationId, out var active)
            ? new[] { active } : _options.PersistentRunnerUrls;
        await Task.WhenAll(endpoints.Select(async endpoint =>
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Delete,
                    endpoint.TrimEnd('/') + "/runs/" + Uri.EscapeDataString(correlationId));
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.PersistentRunnerToken);
                using var response = await clients.CreateClient("persistent-workloads").SendAsync(message, ct);
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) { }
        }));
        if (!_active.ContainsKey(correlationId)) await fallback.CancelAsync(correlationId, ct);
    }
}
