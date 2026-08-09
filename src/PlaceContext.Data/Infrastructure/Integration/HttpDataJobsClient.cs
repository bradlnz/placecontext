using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Data.Integration;

namespace PlaceContext.Data.Infrastructure.Integration;

public sealed class HttpDataJobsClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IDataJobsClient
{
    public async Task<DataJobCatalog> GetCatalogAsync(Guid projectId, CancellationToken ct = default)
    {
        var origin = configuration["PlaceContext:Data:Jobs:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Jobs"]
            ?? throw new InvalidOperationException("Configure the Jobs service destination for Data.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(baseAddress), $"api/jobs/internal/projects/{projectId}/catalog"));
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DataJobCatalog>(ct)
            ?? new DataJobCatalog([], [], []);
    }

    public async Task<JsonElement> RunAsync(
        Guid jobId,
        DataJobRunRequest requestBody,
        CancellationToken ct = default)
    {
        using var request = Request(
            HttpMethod.Post,
            $"api/jobs/internal/jobs/{jobId}/runs",
            JsonContent.Create(requestBody));
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private HttpRequestMessage Request(HttpMethod method, string path, HttpContent? content = null)
    {
        var origin = configuration["PlaceContext:Data:Jobs:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Jobs"]
            ?? throw new InvalidOperationException("Configure the Jobs service destination for Data.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        var request = new HttpRequestMessage(method, new Uri(new Uri(baseAddress), path))
        {
            Content = content,
        };
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }
}
