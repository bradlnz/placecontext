namespace PlaceContext.ClusterHost;

/// <summary>
/// Background service that periodically health-checks every configured shard server
/// and maintains a live list of healthy endpoints. The <see cref="ClusterProxyController"/>
/// picks from this list so unhealthy nodes are skipped automatically.
/// </summary>
public sealed class ClusterProxyService : BackgroundService
{
    private readonly IHttpClientFactory _http;
    private readonly ClusterProxyOptions _opts;
    private readonly ILogger<ClusterProxyService> _log;
    private readonly Dictionary<string, bool> _health = new();

    public IReadOnlyList<string> HealthyEndpoints
    {
        get
        {
            lock (_health)
                return _health.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
        }
    }

    public ClusterProxyService(
        IHttpClientFactory http,
        Microsoft.Extensions.Options.IOptions<ClusterProxyOptions> opts,
        ILogger<ClusterProxyService> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        await CheckHealthAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            await CheckHealthAsync(stoppingToken);
        }
    }

    private async Task CheckHealthAsync(CancellationToken ct)
    {
        var endpoints = _opts.ShardEndpoints.Count > 0
            ? _opts.ShardEndpoints.ToArray()
            : Array.Empty<string>();

        if (endpoints.Length == 0) return;

        var nowHealthy = new List<string>();
        foreach (var ep in endpoints)
        {
            try
            {
                var client = _http.CreateClient();
                if (!string.IsNullOrWhiteSpace(_opts.ApiToken))
                    client.DefaultRequestHeaders.TryAddWithoutValidation(
                        ClusterApiAuthenticationMiddleware.HeaderName, _opts.ApiToken);
                client.Timeout = TimeSpan.FromSeconds(5);
                var resp = await client.GetAsync($"{ep}/health", ct);
                if (resp.IsSuccessStatusCode)
                {
                    nowHealthy.Add(ep);
                    _health[ep] = true;
                }
                else
                {
                    _health[ep] = false;
                }
            }
            catch
            {
                _health[ep] = false;
            }
        }

        if (nowHealthy.Count > 0)
            _log.LogInformation("Cluster healthy: {Healthy}/{Total} shards available", nowHealthy.Count, endpoints.Length);
        else
            _log.LogWarning("No shard servers healthy");
    }
}
