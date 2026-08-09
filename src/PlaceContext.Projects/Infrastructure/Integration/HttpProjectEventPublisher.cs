using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Projects.Integration;

namespace PlaceContext.Projects.Infrastructure.Integration;

public sealed class HttpProjectEventPublisher(IHttpClientFactory clients, IConfiguration configuration) : IProjectEventPublisher
{
    public async Task RaiseAsync(string eventType, Guid projectId, string payload, CancellationToken ct = default)
    {
        var origin = configuration["PlaceContext:Projects:Jobs:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Jobs"];
        var apiKey = configuration["PlaceContext:Api:Key"];
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(apiKey)) return;
        var root = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(root), "api/jobs/internal/events"))
        {
            Content = JsonContent.Create(new { eventType, projectId, payload }),
        };
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await clients.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
