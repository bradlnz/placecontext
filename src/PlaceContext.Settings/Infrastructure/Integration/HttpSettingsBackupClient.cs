using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Settings.Integration;

namespace PlaceContext.Settings.Infrastructure.Integration;

public sealed class HttpSettingsBackupClient(IHttpClientFactory clients, IConfiguration configuration) : ISettingsBackupClient
{
    public async Task<JsonElement> ImportAsync(JsonElement manifest, CancellationToken ct = default)
    {
        var origin = configuration["PlaceContext:Settings:Operations:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Operations"]
            ?? throw new InvalidOperationException("Configure the Operations service destination for Settings.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(origin.TrimEnd('/') + "/"), "api/backup/import"));
        request.Headers.Add("X-Api-Key", apiKey);
        request.Content = JsonContent.Create(manifest);
        using var response = await clients.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }
}
