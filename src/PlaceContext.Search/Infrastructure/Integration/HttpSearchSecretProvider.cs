using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Search.Integration;

namespace PlaceContext.Search.Infrastructure.Integration;

public sealed class HttpSearchSecretProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ISearchSecretProvider
{
    public async Task<IReadOnlyDictionary<string, string>> GetSecretsAsync(
        Guid projectId,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default)
    {
        var origin = configuration["PlaceContext:Search:Vault:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Vault"]
            ?? throw new InvalidOperationException("Configure the Vault service destination for Search.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(baseAddress), $"api/vault/internal/projects/{projectId}/secrets/resolve"))
        {
            Content = JsonContent.Create(new ResolveSecretsRequest(names)),
        };
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken)
            ?? new Dictionary<string, string>();
    }

    private sealed record ResolveSecretsRequest(IReadOnlyList<string> Names);
}
