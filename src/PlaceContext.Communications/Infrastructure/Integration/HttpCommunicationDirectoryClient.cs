using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace PlaceContext.Communications.Infrastructure.Integration;

public sealed class HttpCommunicationDirectoryClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ICommunicationDirectoryClient
{
    public Task<JsonElement> ListProjectsAsync(CancellationToken ct = default)
        => GetAsync("Projects", "api/projects/internal", ct);

    public Task<JsonElement> ListSecretsAsync(Guid projectId, CancellationToken ct = default)
        => GetAsync("Vault", $"api/vault/internal/projects/{projectId}/secrets", ct);

    private async Task<JsonElement> GetAsync(string service, string path, CancellationToken ct)
    {
        var origin = configuration[$"PlaceContext:Communications:{service}:BaseAddress"]
            ?? configuration[$"PlaceContext:Microservices:Destinations:{service}"]
            ?? throw new InvalidOperationException(
                $"Configure the {service} service destination for Communications.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(baseAddress), path));
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct);
        return document.RootElement.Clone();
    }
}
