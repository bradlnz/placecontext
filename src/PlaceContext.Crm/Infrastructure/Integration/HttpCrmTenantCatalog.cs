using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Ports;

namespace PlaceContext.Crm.Infrastructure.Integration;

public sealed class HttpCrmTenantCatalog(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ITenantCatalog
{
    public async Task<IReadOnlyList<TenantContext>> ListAsync(CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "api/identity/internal/tenants");
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<TenantContext>>(ct) ?? [];
    }

    public async Task<TenantContext?> FindAsync(Guid tenantId, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/identity/internal/tenants/{tenantId}");
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TenantContext>(ct);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var origin = configuration["PlaceContext:Crm:Identity:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Identity"]
            ?? throw new InvalidOperationException("Configure the Identity service destination for CRM.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        var request = new HttpRequestMessage(method, new Uri(new Uri(baseAddress), relativePath));
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }
}
