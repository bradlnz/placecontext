using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace PlaceContext.Communications.Infrastructure.Integration;

public sealed class HttpCommunicationVaultClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ICommunicationVaultClient
{
    public async Task<string?> ResolveAsync(
        Guid projectId,
        string name,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            $"api/vault/internal/projects/{projectId}/secrets/resolve");
        request.Content = JsonContent.Create(new { names = new[] { name } });
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var values = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(ct);
        return values is not null && values.TryGetValue(name, out var value) ? value : null;
    }

    public async Task<bool> ExistsAsync(
        Guid projectId,
        string name,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"api/vault/internal/projects/{projectId}/secrets");
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct);
        return document.RootElement.ValueKind == JsonValueKind.Array
            && document.RootElement.EnumerateArray().Any(secret =>
                secret.TryGetProperty("name", out var property)
                && string.Equals(property.GetString(), name, StringComparison.Ordinal));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var origin = configuration["PlaceContext:Communications:Vault:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Vault"]
            ?? throw new InvalidOperationException("Configure the Vault service destination for Communications.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        var request = new HttpRequestMessage(method, new Uri(new Uri(baseAddress), relativePath));
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }
}
