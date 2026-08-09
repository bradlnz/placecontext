using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Crm.Integration;

namespace PlaceContext.Crm.Infrastructure.Integration;

public sealed class HttpCrmDataClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ICrmDataClient
{
    public async Task InsertRowAsync(
        Guid projectId,
        string tableName,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken ct = default)
    {
        var origin = configuration["PlaceContext:Crm:Data:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Data"]
            ?? throw new InvalidOperationException("Configure the Data service destination for CRM.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(baseAddress), $"api/data/internal/projects/{projectId}/rows"));
        request.Headers.Add("X-Api-Key", apiKey);
        request.Content = JsonContent.Create(new { tableName, values });
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
