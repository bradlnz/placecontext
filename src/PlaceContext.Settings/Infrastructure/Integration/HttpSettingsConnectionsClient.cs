using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Settings.Integration;

namespace PlaceContext.Settings.Infrastructure.Integration;

public sealed class HttpSettingsConnectionsClient(IHttpClientFactory clients, IConfiguration configuration)
    : ISettingsConnectionsClient
{
    public async Task<IReadOnlyList<SettingsProject>> ListProjectsAsync(CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get, "Projects", "api/projects/internal", null, ct);
        return json.EnumerateArray().Select(item => new SettingsProject(
            item.GetProperty("id").GetGuid(), item.GetProperty("name").GetString() ?? string.Empty)).ToList();
    }

    public async Task<IReadOnlyList<string>> ListSecretNamesAsync(Guid projectId, CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get, "Vault", $"api/vault/internal/projects/{projectId}/secrets", null, ct);
        return json.EnumerateArray().Select(item => item.GetProperty("name").GetString() ?? string.Empty).ToList();
    }

    public Task SetSecretAsync(Guid projectId, string name, string value, CancellationToken ct = default)
        => SendWithoutResultAsync(HttpMethod.Post, "Vault", $"api/vault/projects/{projectId}/secrets", new { name, value }, ct);

    public Task DeleteSecretAsync(Guid projectId, string name, CancellationToken ct = default)
        => SendWithoutResultAsync(HttpMethod.Delete, "Vault", $"api/vault/projects/{projectId}/secrets/{Uri.EscapeDataString(name)}", null, ct);

    private async Task SendWithoutResultAsync(HttpMethod method, string service, string path, object? body, CancellationToken ct)
        => _ = await SendAsync(method, service, path, body, ct);

    private async Task<JsonElement> SendAsync(HttpMethod method, string service, string path, object? body, CancellationToken ct)
    {
        var origin = configuration[$"PlaceContext:Settings:{service}:BaseAddress"]
            ?? configuration[$"PlaceContext:Microservices:Destinations:{service}"]
            ?? throw new InvalidOperationException($"Configure the {service} service destination for Settings.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        using var request = new HttpRequestMessage(method, new Uri(new Uri(origin.TrimEnd('/') + "/"), path));
        request.Headers.Add("X-Api-Key", apiKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await clients.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength == 0) return JsonDocument.Parse("null").RootElement.Clone();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }
}
