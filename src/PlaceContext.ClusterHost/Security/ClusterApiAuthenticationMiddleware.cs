using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace PlaceContext.ClusterHost;

/// <summary>
/// Protects the internal cluster compute surface with the shared token provisioned by the
/// installer. The health route remains unauthenticated for Kubernetes probes and exposes only
/// aggregate state.
/// </summary>
public sealed class ClusterApiAuthenticationMiddleware
{
    public const string HeaderName = "X-PlaceContext-AI-Token";

    private readonly RequestDelegate _next;

    public ClusterApiAuthenticationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IOptions<ClusterProxyOptions> options)
    {
        if (!context.Request.Path.StartsWithSegments("/api/cluster")
            || context.Request.Path.Equals("/api/cluster/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var configured = options.Value.ApiToken;
        if (string.IsNullOrWhiteSpace(configured))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Cluster API authentication is not configured." });
            return;
        }

        var supplied = context.Request.Headers[HeaderName].ToString();
        if (!SecureEquals(supplied, configured))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized." });
            return;
        }

        await _next(context);
    }

    private static bool SecureEquals(string supplied, string configured)
    {
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(supplied ?? string.Empty));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
