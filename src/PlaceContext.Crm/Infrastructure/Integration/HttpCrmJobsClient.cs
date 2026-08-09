using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Crm.Integration;

namespace PlaceContext.Crm.Infrastructure.Integration;

public sealed class HttpCrmJobsClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ICrmJobsClient
{
    public async Task<CrmJobsCatalog> GetCatalogAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"api/jobs/internal/projects/{projectId}/catalog");
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CrmJobsCatalog>(ct)
            ?? new CrmJobsCatalog([]);
    }

    public async Task<CrmJobChainRun> RunChainAsync(
        CrmRunJobChainRequest request,
        CancellationToken ct = default)
    {
        using var message = CreateRequest(
            HttpMethod.Post,
            $"api/jobs/internal/chains/{request.ChainId}/runs");
        message.Content = JsonContent.Create(request);
        using var response = await httpClientFactory.CreateClient().SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CrmJobChainRun>(ct)
            ?? throw new InvalidOperationException("The Jobs service returned an empty chain-run response.");
    }

    public async Task<CrmJobChainRun?> GetRunAsync(
        Guid chainRunId,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"api/jobs/internal/chain-runs/{chainRunId}");
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CrmJobChainRun>(ct);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var origin = configuration["PlaceContext:Crm:Jobs:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Jobs"]
            ?? throw new InvalidOperationException("Configure the Jobs service destination for CRM.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        var request = new HttpRequestMessage(method, new Uri(new Uri(baseAddress), relativePath));
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }
}
