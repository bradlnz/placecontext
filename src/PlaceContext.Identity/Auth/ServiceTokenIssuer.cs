using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PlaceContext.Application.Ports;

namespace PlaceContext.Identity.Auth;

public sealed class ServiceTokenIssuer(
    IConfiguration configuration,
    IPermissionService permissions,
    ICurrentTenant tenant)
{
    public async Task<(string Token, DateTimeOffset ExpiresAt)> IssueAsync(
        ClaimsPrincipal principal,
        CancellationToken ct = default)
    {
        var signingKey = configuration["PlaceContext:ServiceAuth:SigningKey"]
            ?? throw new InvalidOperationException(
                "Configure PlaceContext:ServiceAuth:SigningKey for edge service tokens.");
        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException(
                "PlaceContext:ServiceAuth:SigningKey must contain at least 32 UTF-8 bytes.");

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(5);
        var claims = principal.Claims
            .Where(claim => claim.Type is ClaimTypes.NameIdentifier
                or ClaimTypes.Name
                or ClaimTypes.Email
                or ClaimTypes.Role
                or "sub"
                or "role"
                or "tenant")
            .Select(claim => new Claim(claim.Type, claim.Value))
            .ToList();
        if (tenant.IsResolved)
        {
            Replace(claims, "tenant", tenant.TenantId.ToString());
            Replace(claims, "tenant_slug", tenant.Slug);
            Replace(claims, "tenant_timezone", tenant.TimeZoneId);
        }
        foreach (var permission in await permissions.GetEffectivePermissionsAsync(ct))
            claims.Add(new Claim("permission", permission));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = configuration["PlaceContext:ServiceAuth:Issuer"] ?? "placecontext",
            Audience = configuration["PlaceContext:ServiceAuth:Audience"] ?? "placecontext-services",
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }

    private static void Replace(List<Claim> claims, string type, string value)
    {
        claims.RemoveAll(claim => claim.Type == type);
        claims.Add(new Claim(type, value));
    }
}
