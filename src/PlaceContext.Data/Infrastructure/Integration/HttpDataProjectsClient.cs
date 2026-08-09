using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Data.Integration;

namespace PlaceContext.Data.Infrastructure.Integration;

public sealed class HttpDataProjectsClient(
    IHttpClientFactory httpClientFactory, IConfiguration configuration) : IDataProjectsClient
{
    public async Task<IReadOnlyList<DataProjectSummary>> ListAsync(CancellationToken ct = default)
    {
        var origin = configuration["PlaceContext:Data:Projects:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Projects"]
            ?? throw new InvalidOperationException("Configure the Projects service destination for Data.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(baseAddress), "api/projects/internal"));
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<DataProjectSummary>>(ct) ?? [];
    }
}
