using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace PlaceContext.Host.Auth;

public static class AgentAuthenticationDefaults
{
    public const string SchemeName = "AgentUser";

    public static string SelectScheme(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer pct_", StringComparison.Ordinal))
            return UserApiTokenAuthenticationHandler.SchemeName;

        var apiKey = context.Request.Headers["X-Api-Key"].ToString();
        return apiKey.StartsWith("pct_", StringComparison.Ordinal)
            ? UserApiTokenAuthenticationHandler.SchemeName
            : JwtBearerDefaults.AuthenticationScheme;
    }
}
