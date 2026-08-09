using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using PlaceContext.Application.Ports;
using PlaceContext.Identity.Auth;

namespace PlaceContext.Identity.Tests;

public sealed class ServiceTokenIssuerTests
{
    [Fact]
    public async Task Issues_a_short_lived_service_token_with_tenant_user_and_permissions()
    {
        var tenant = new StubTenant(
            Guid.Parse("24e6a91e-86b9-4ddb-b468-681d111dcf00"),
            "acme",
            "Australia/Brisbane");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["PlaceContext:ServiceAuth:SigningKey"] =
                    "a-service-token-test-key-with-at-least-32-bytes",
                ["PlaceContext:ServiceAuth:Issuer"] = "issuer.test",
                ["PlaceContext:ServiceAuth:Audience"] = "audience.test",
            }).Build();
        var issuer = new ServiceTokenIssuer(
            configuration,
            new StubPermissions("jobs.view", "jobs.run"),
            tenant);
        var userId = Guid.Parse("8c52f5f2-18be-42b0-8623-c08fe6176189");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("tenant", Guid.Empty.ToString()),
        ], "cookie"));

        var before = DateTimeOffset.UtcNow;
        var (encoded, expiresAt) = await issuer.IssueAsync(principal);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(encoded);

        Assert.Equal("issuer.test", token.Issuer);
        Assert.Contains("audience.test", token.Audiences);
        Assert.Equal(userId.ToString(), token.GetClaim(ClaimTypes.NameIdentifier).Value);
        Assert.Equal(tenant.TenantId.ToString(), token.GetClaim("tenant").Value);
        Assert.Equal("acme", token.GetClaim("tenant_slug").Value);
        Assert.Equal("Australia/Brisbane", token.GetClaim("tenant_timezone").Value);
        Assert.Equal(
            ["jobs.run", "jobs.view"],
            token.Claims.Where(claim => claim.Type == "permission")
                .Select(claim => claim.Value)
                .OrderBy(value => value));
        Assert.InRange(expiresAt, before.AddMinutes(4.9), before.AddMinutes(5.1));
    }

    private sealed class StubPermissions(params string[] permissions) : IPermissionService
    {
        private readonly IReadOnlySet<string> _permissions = permissions.ToHashSet();

        public Task<bool> HasAsync(string permission, CancellationToken ct = default)
            => Task.FromResult(_permissions.Contains(permission));

        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default)
            => Task.FromResult(_permissions);

        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(
            Guid userId,
            string roleName,
            CancellationToken ct = default)
            => Task.FromResult(_permissions);
    }

    private sealed record StubTenant(
        Guid TenantId,
        string Slug,
        string TimeZoneId) : ICurrentTenant
    {
        public bool IsResolved => true;
    }
}
