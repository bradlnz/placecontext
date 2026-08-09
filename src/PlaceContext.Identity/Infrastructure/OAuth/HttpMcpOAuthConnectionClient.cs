using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PlaceContext.Application.Ports;
using PlaceContext.ServiceDefaults;
using PlaceContext.Identity.OAuth;

namespace PlaceContext.Identity.Infrastructure.OAuth;

public sealed class HttpMcpOAuthConnectionClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IMcpOAuthConnectionClient
{
    public async Task<McpOAuthConnectionContext?> GetAsync(
        Guid connectionId,
        IdentityTenantContext tenant,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"api/mcp/internal/oauth/connections/{connectionId:D}",
            tenant);
        using var response = await SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<McpOAuthConnectionContext>(ct);
    }

    public async Task StoreTokensAsync(
        Guid connectionId,
        StoreMcpOAuthTokensRequest request,
        IdentityTenantContext tenant,
        CancellationToken ct = default)
    {
        using var message = CreateRequest(
            HttpMethod.Put,
            $"api/mcp/internal/oauth/connections/{connectionId:D}/tokens",
            tenant);
        message.Content = JsonContent.Create(request);
        using var response = await SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateStatusAsync(
        Guid connectionId,
        UpdateMcpOAuthStatusRequest request,
        IdentityTenantContext tenant,
        CancellationToken ct = default)
    {
        using var message = CreateRequest(
            HttpMethod.Put,
            $"api/mcp/internal/oauth/connections/{connectionId:D}/status",
            tenant);
        message.Content = JsonContent.Create(request);
        using var response = await SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string relativePath,
        IdentityTenantContext tenant)
    {
        var origin = configuration["PlaceContext:Identity:Mcp:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Mcp"]
            ?? throw new InvalidOperationException(
                "Configure PlaceContext:Identity:Mcp:BaseAddress for the Identity-to-MCP service contract.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        var request = new HttpRequestMessage(method, new Uri(new Uri(baseAddress), relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateServiceToken(tenant));
        return request;
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => httpClientFactory.CreateClient().SendAsync(request, ct);

    private string CreateServiceToken(IdentityTenantContext tenant)
    {
        var configuredToken = configuration["PlaceContext:Identity:Mcp:ServiceToken"];
        if (!string.IsNullOrWhiteSpace(configuredToken))
            return configuredToken;

        var signingKey = configuration[$"{ServiceAuthenticationDefaults.SectionName}:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException(
                "Identity requires either PlaceContext:Identity:Mcp:ServiceToken or a ServiceAuth signing key.");

        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, tenant.UserId.ToString()),
            new Claim("tenant", tenant.TenantId.ToString()),
            new Claim("tenant_slug", tenant.TenantSlug),
            new Claim("tenant_timezone", tenant.TenantTimeZone),
            new Claim(ServiceAuthenticationDefaults.PermissionClaim, Permission.SettingsManage),
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: configuration[$"{ServiceAuthenticationDefaults.SectionName}:Issuer"] ?? "placecontext",
            audience: configuration[$"{ServiceAuthenticationDefaults.SectionName}:Audience"] ?? "placecontext-services",
            claims: claims,
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(2),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
