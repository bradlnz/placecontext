using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Ports;

namespace PlaceContext.Jobs.Infrastructure.Integration;

public sealed class HttpJobTenantCatalog(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ITenantCatalog
{
    public async Task<IReadOnlyList<TenantContext>> ListAsync(CancellationToken ct = default)
        => await SendAsync<IReadOnlyList<TenantContext>>("tenants", ct) ?? [];

    public Task<TenantContext?> FindAsync(Guid tenantId, CancellationToken ct = default)
        => SendAsync<TenantContext>($"tenants/{tenantId}", ct);

    private async Task<T?> SendAsync<T>(string path, CancellationToken ct)
    {
        var origin = configuration["PlaceContext:Jobs:Identity:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Identity"]
            ?? throw new InvalidOperationException("Configure the Identity service destination for Jobs.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(baseAddress), $"api/identity/internal/{path}"));
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }
}
