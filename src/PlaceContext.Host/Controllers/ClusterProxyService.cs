using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Background service that periodically health-checks every configured shard server
/// and maintains a live list of healthy endpoints. The <see cref="ClusterProxyController"/>
/// picks from this list (round-robin) so unhealthy nodes are skipped automatically.
/// </summary>
public sealed class ClusterProxyService : BackgroundService
{
    private readonly IHttpClientFactory _http;
    private readonly ClusterProxyOptions _opts;
    private readonly ILogger<ClusterProxyService> _log;

    // Thread-safe set of endpoints currently responding to /health.
    private readonly ConcurrentBag<string> _healthy = new();

    public ClusterProxyService(
        IHttpClientFactory http,
        IOptions<ClusterProxyOptions> opts,
        ILogger<ClusterProxyService> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;
    }

    /// <summary>Return the current set of healthy shard server URLs.</summary>
    public IReadOnlyCollection<string> HealthyEndpoints =>
        _healthy.ToArray() is { Length: > 0 } h
            ? h
            : _opts.ShardEndpoints.ToArray(); // fallback to all configured while initial check hasn't run

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First check runs immediately so the controller isn't starved on startup.
        await CheckOnce(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            await CheckOnce(stoppingToken);
        }
    }

    private async Task CheckOnce(CancellationToken ct)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        var nowHealthy = new List<string>();

        foreach (var ep in _opts.ShardEndpoints)
        {
            var url = ep.TrimEnd('/') + "/health";
            try
            {
                var resp = await client.GetAsync(url, ct);
                if (resp.IsSuccessStatusCode)
                {
                    nowHealthy.Add(ep.TrimEnd('/'));
                    _log.LogDebug("Shard {Endpoint} healthy", ep);
                }
                else
                {
                    _log.LogWarning("Shard {Endpoint} returned {Status}", ep, resp.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("Shard {Endpoint} unreachable: {Error}", ep, ex.Message);
            }
        }

        // Swap the healthy list atomically.
        while (_healthy.TryTake(out _)) { }
        foreach (var ep in nowHealthy) _healthy.Add(ep);

        if (nowHealthy.Count == 0 && _opts.ShardEndpoints.Count > 0)
            _log.LogError("All {Count} shard servers are unreachable!", _opts.ShardEndpoints.Count);
        else
            _log.LogInformation("Cluster healthy: {Healthy}/{Total} shards available",
                nowHealthy.Count, _opts.ShardEndpoints.Count);
    }
}
