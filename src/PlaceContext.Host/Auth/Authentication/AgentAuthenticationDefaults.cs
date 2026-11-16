using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;

namespace PlaceContext.Host.Auth;

public static class AgentAuthenticationDefaults
{
    public const string SchemeName = "AgentUser";

    public static bool CanAccessMcp(ClaimsPrincipal user) => user.Identities.Any(identity => identity.IsAuthenticated
        && (identity.AuthenticationType == UserApiTokenAuthenticationHandler.SchemeName
            || identity.FindAll("scope").Any(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("mcp", StringComparer.Ordinal))));

    public static string SelectScheme(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer pct_", StringComparison.Ordinal))
        {
            return UserApiTokenAuthenticationHandler.SchemeName;
        }
        var apiKey = context.Request.Headers["X-Api-Key"].ToString();
        return apiKey.StartsWith("pct_", StringComparison.Ordinal)
            ? UserApiTokenAuthenticationHandler.SchemeName
            : JwtBearerDefaults.AuthenticationScheme;
    }
}
