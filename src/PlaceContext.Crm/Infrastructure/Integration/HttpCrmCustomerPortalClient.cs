using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Crm.Integration;

namespace PlaceContext.Crm.Infrastructure.Integration;

public sealed class HttpCrmCustomerPortalClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ICrmCustomerPortalClient
{
    public async Task ProvisionAsync(
        Guid tenantId,
        string slug,
        string? customDomain,
        string? brandName,
        string? brandLogoUrl,
        string? defaultPortalUserName = null,
        string? defaultPortalUserEmail = null,
        string? defaultPortalUserPassword = null,
        CancellationToken ct = default)
    {
        var origin = configuration["PlaceContext:Crm:Operations:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Operations"]
            ?? throw new InvalidOperationException("Configure the Operations service destination for CRM.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(baseAddress), "api/operations/internal/customer-portals"));
        request.Headers.Add("X-Api-Key", apiKey);
        request.Content = JsonContent.Create(new
        {
            tenantId,
            slug,
            customDomain,
            brandName,
            brandLogoUrl,
            defaultPortalUserName,
            defaultPortalUserEmail,
            defaultPortalUserPassword,
        });
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
