using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Projects.Integration;

namespace PlaceContext.Projects.Infrastructure.Integration;

public sealed class HttpProjectGraphClient(IHttpClientFactory clients, IConfiguration configuration) : IProjectGraphClient
{
    public async Task<IReadOnlyList<ProjectGraphHotspot>> GetHotspotsAsync(Guid projectId, CancellationToken ct = default)
    {
        using var request = Request("Data", HttpMethod.Get, $"api/data/internal/projects/{projectId}/graph-hotspots");
        using var response = await clients.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ProjectGraphHotspot>>(ct) ?? [];
    }

    private HttpRequestMessage Request(string service, HttpMethod method, string path)
    {
        var origin = configuration[$"PlaceContext:Projects:{service}:BaseAddress"]
            ?? configuration[$"PlaceContext:Microservices:Destinations:{service}"]
            ?? throw new InvalidOperationException($"Configure the {service} service destination for Projects.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var root = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        var request = new HttpRequestMessage(method, new Uri(new Uri(root), path));
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }
}
