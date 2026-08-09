using Microsoft.Extensions.Configuration;
using PlaceContext.Crm.Integration;

namespace PlaceContext.Crm.Infrastructure.Integration;

public sealed class HttpCrmProjectsClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ICrmProjectsClient
{
    public async Task<bool> ExistsAsync(Guid projectId, CancellationToken ct = default)
    {
        var origin = configuration["PlaceContext:Crm:Projects:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Projects"]
            ?? throw new InvalidOperationException("Configure the Projects service destination for CRM.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(baseAddress), $"api/projects/internal/{projectId}/exists"));
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }
}
