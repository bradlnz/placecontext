using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Build;

public sealed class LocalModelDiscovery(IServiceScopeFactory scopes, ICurrentTenant tenant, IHttpClientFactory clients,
    IHostEnvironment environment, IConfiguration config)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _checked = new();
    private static readonly (string Name, string Protocol, string Url)[] Providers =
    [
        ("Ollama (local)", "openai", "http://127.0.0.1:11434/v1"),
        ("LM Studio (local)", "openai", "http://127.0.0.1:1234/v1"),
        ("Codex", "codex", "cli://codex"),
        ("Claude Code", "claude-code", "cli://claude")
    ];

    public async Task DetectAsync(CancellationToken ct = default)
    {
        if (!tenant.IsResolved || !config.GetValue("PlaceContext:Build:DetectLocalModels", environment.IsDevelopment()))
        {
            return;
        }
        await _gate.WaitAsync(ct);
        try
        {
            var tenantId = tenant.TenantId;
            if (_checked.TryGetValue(tenantId, out var last) && last > DateTimeOffset.UtcNow.AddSeconds(-30))
            {
                return;
            }
            await using var scope = scopes.CreateAsyncScope();
            var cli = scope.ServiceProvider.GetService<LocalCliModels>();
            var models = await Task.WhenAll(Providers.Select(async x => LocalCliModels.Supports(x.Protocol)
                ? cli is null || tenant.Slug != "default" ? null : await cli.DiscoverAsync(x.Protocol, ct)
                : await ProbeAsync(x.Url, ct)));
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.ModelConnections.ToListAsync(ct);
            for (var i = 0; i < Providers.Length; i++)
            {
                var provider = Providers[i];
                var row = rows.FirstOrDefault(x => x.BaseUrl == provider.Url || SameLocalEndpoint(x.BaseUrl, provider.Url));
                if (row is not null && (!row.AutoDiscovered || !row.Enabled))
                {
                    continue;
                }
                if (row is null)
                {
                    if (models[i] is null || (models[i]!.Length == 0 && !LocalCliModels.Supports(provider.Protocol)))
                    {
                        continue;
                    }
                    var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}:{provider.Url}")).AsSpan(0, 16));
                    // A manually edited discovery entry keeps its identity and settings.
                    if (rows.Any(x => x.Id == id))
                    {
                        continue;
                    }
                    row = new()
                    {
                        Id = id,
                        Name = provider.Name,
                        Protocol = provider.Protocol,
                        BaseUrl = provider.Url,
                        AutoDiscovered = true
                    };
                    db.ModelConnections.Add(row);
                }
                row.ModelsJson = JsonSerializer.Serialize(models[i] ?? []);
            }
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { }
            _checked[tenantId] = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool SameLocalEndpoint(string left, string right) =>
        Uri.TryCreate(left, UriKind.Absolute, out var a) && Uri.TryCreate(right, UriKind.Absolute, out var b)
        && a.IsLoopback && a.Scheme == b.Scheme && a.Port == b.Port && a.AbsolutePath.TrimEnd('/') == b.AbsolutePath.TrimEnd('/');

    private async Task<string[]> ProbeAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var client = clients.CreateClient("build-model");
            using var response = await client.GetAsync(url + "/models", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }
            await response.Content.LoadIntoBufferAsync(2_000_000, timeout.Token);
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return (body?["data"] as JsonArray ?? []).Select(x => x?["id"])
                .OfType<JsonValue>().Select(x => x.TryGetValue<string>(out var id) ? id : null)
                .OfType<string>().Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 200).Distinct().Order().Take(500).ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            return [];
        }
    }
}
