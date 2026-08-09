using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Jobs.Integration;

namespace PlaceContext.Jobs.Infrastructure.Integration;

public sealed class HttpJobCrmClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IJobCrmClient
{
    public async Task<JobCrmCustomer?> GetCustomerAsync(
        Guid id,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/crm/internal/customers/{id}");
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobCrmCustomer>(ct);
    }

    public async Task NotifyChainCompletedAsync(
        JobCrmChainCompletion completion,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/crm/internal/chain-runs/completed");
        request.Content = JsonContent.Create(completion);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var origin = configuration["PlaceContext:Jobs:Crm:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Crm"]
            ?? throw new InvalidOperationException("Configure the CRM service destination for Jobs.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        var request = new HttpRequestMessage(method, new Uri(new Uri(baseAddress), relativePath));
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }
}
