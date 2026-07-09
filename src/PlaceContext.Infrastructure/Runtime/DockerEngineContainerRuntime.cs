using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Runtime;

/// <summary>
/// <see cref="IContainerRuntime"/> over the Docker Engine HTTP API of the in-cluster DinD daemon
/// (PlaceContext:Runtime:DockerEndpoint, e.g. http://placecontext-dind:2375 — cluster-internal
/// only). Project isolation is enforced twice: listings filter on the
/// <c>placecontext.project=&lt;guid&gt;</c> label, and every per-container operation first inspects
/// the container and verifies it carries the requesting project's label.
/// </summary>
public sealed class DockerEngineContainerRuntime : IContainerRuntime
{
    internal const string ProjectLabel = "placecontext.project";

    private readonly IHttpClientFactory _http;
    private readonly string? _endpoint;

    public DockerEngineContainerRuntime(IHttpClientFactory http, IConfiguration config)
    {
        _http = http;
        _endpoint = config["PlaceContext:Runtime:DockerEndpoint"]?.TrimEnd('/');
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_endpoint);

    private HttpClient Client()
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        var filters = Uri.EscapeDataString(JsonSerializer.Serialize(new
        {
            label = new[] { $"{ProjectLabel}={projectId:D}" },
        }));
        using var resp = await Client().GetAsync($"{_endpoint}/containers/json?all=true&filters={filters}", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        var containers = new List<ContainerInfo>();
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            var name = c.TryGetProperty("Names", out var names) && names.GetArrayLength() > 0
                ? names[0].GetString()!.TrimStart('/')
                : c.GetProperty("Id").GetString()![..12];
            var ports = new List<string>();
            var published = new List<int>();
            if (c.TryGetProperty("Ports", out var portArr))
                foreach (var p in portArr.EnumerateArray())
                {
                    var priv = p.TryGetProperty("PrivatePort", out var pp) ? pp.GetInt32() : 0;
                    var pub = p.TryGetProperty("PublicPort", out var pb) ? pb.GetInt32() : 0;
                    var type = p.TryGetProperty("Type", out var t) ? t.GetString() : "tcp";
                    var display = pub > 0 ? $"{pub}→{priv}/{type}" : $"{priv}/{type}";
                    if (!ports.Contains(display)) ports.Add(display);
                    if (pub > 0 && type == "tcp" && !published.Contains(pub)) published.Add(pub);
                }
            containers.Add(new ContainerInfo(
                c.GetProperty("Id").GetString()!,
                name,
                c.TryGetProperty("Image", out var img) ? img.GetString() ?? "" : "",
                c.TryGetProperty("State", out var st) ? st.GetString() ?? "" : "",
                c.TryGetProperty("Status", out var stat) ? stat.GetString() ?? "" : "",
                ports, published));
        }
        return containers
            .OrderByDescending(c => c.State == "running")
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string> LogsAsync(Guid projectId, string containerId, int tail = 200, CancellationToken ct = default)
    {
        await EnsureOwnedAsync(projectId, containerId, ct);
        using var resp = await Client().GetAsync(
            $"{_endpoint}/containers/{Uri.EscapeDataString(containerId)}/logs?stdout=true&stderr=true&tail={tail}", ct);
        resp.EnsureSuccessStatusCode();
        return Demux(await resp.Content.ReadAsByteArrayAsync(ct));
    }

    public async Task RestartAsync(Guid projectId, string containerId, CancellationToken ct = default)
    {
        await EnsureOwnedAsync(projectId, containerId, ct);
        using var resp = await Client().PostAsync(
            $"{_endpoint}/containers/{Uri.EscapeDataString(containerId)}/restart?t=10", content: null, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task StopAsync(Guid projectId, string containerId, CancellationToken ct = default)
    {
        await EnsureOwnedAsync(projectId, containerId, ct);
        using var resp = await Client().PostAsync(
            $"{_endpoint}/containers/{Uri.EscapeDataString(containerId)}/stop?t=10", content: null, ct);
        // 304 = already stopped — that's the state the caller wanted.
        if (resp.StatusCode != System.Net.HttpStatusCode.NotModified)
            resp.EnsureSuccessStatusCode();
    }

    // Inspect the container and require the requesting project's label — the second isolation wall
    // (the first is the list filter): a guessed/leaked container id from another project 404s here.
    private async Task EnsureOwnedAsync(Guid projectId, string containerId, CancellationToken ct)
    {
        using var resp = await Client().GetAsync(
            $"{_endpoint}/containers/{Uri.EscapeDataString(containerId)}/json", ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Container {containerId} not found.");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var owned = doc.RootElement.TryGetProperty("Config", out var cfg)
            && cfg.TryGetProperty("Labels", out var labels)
            && labels.ValueKind == JsonValueKind.Object
            && labels.TryGetProperty(ProjectLabel, out var label)
            && string.Equals(label.GetString(), projectId.ToString("D"), StringComparison.OrdinalIgnoreCase);
        if (!owned)
            throw new InvalidOperationException($"Container {containerId} does not belong to this project.");
    }

    /// <summary>
    /// Docker multiplexes non-TTY logs as 8-byte-header frames: [stream, 0, 0, 0, len(4, BE)].
    /// Strip the headers; if the payload doesn't look framed (TTY container), pass it through.
    /// </summary>
    internal static string Demux(byte[] raw)
    {
        if (raw.Length == 0) return "";
        // Frame headers start with stream id 0/1/2 followed by three zero bytes.
        var framed = raw.Length >= 8 && raw[0] <= 2 && raw[1] == 0 && raw[2] == 0 && raw[3] == 0;
        if (!framed) return Encoding.UTF8.GetString(raw);

        var sb = new StringBuilder();
        var pos = 0;
        while (pos + 8 <= raw.Length)
        {
            var len = (raw[pos + 4] << 24) | (raw[pos + 5] << 16) | (raw[pos + 6] << 8) | raw[pos + 7];
            pos += 8;
            if (len <= 0 || pos + len > raw.Length) { sb.Append(Encoding.UTF8.GetString(raw, pos, raw.Length - pos)); break; }
            sb.Append(Encoding.UTF8.GetString(raw, pos, len));
            pos += len;
        }
        return sb.ToString();
    }
}
