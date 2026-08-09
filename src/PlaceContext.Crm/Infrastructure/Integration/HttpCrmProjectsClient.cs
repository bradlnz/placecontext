using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Crm.Integration;

namespace PlaceContext.Crm.Infrastructure.Integration;

public sealed class HttpCrmProjectsClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ICrmProjectsClient
{
    public async Task<IReadOnlyList<CrmProjectSummary>> ListAsync(
        CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "api/projects/internal");
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<CrmProjectSummary>>(ct) ?? [];
    }

    public async Task<bool> ExistsAsync(Guid projectId, CancellationToken ct = default)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"api/projects/internal/{projectId}/exists");
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var origin = configuration["PlaceContext:Crm:Projects:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Projects"]
            ?? throw new InvalidOperationException("Configure the Projects service destination for CRM.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        var request = new HttpRequestMessage(method, new Uri(new Uri(baseAddress), relativePath));
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }
}
