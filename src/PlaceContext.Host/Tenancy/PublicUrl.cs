namespace PlaceContext.Host.Tenancy;

/// <summary>
/// Builds public absolute URLs for OAuth metadata and challenges. Each deployment is
/// client-isolated on the client's own hardware, so the request <c>Host</c> is trusted directly;
/// set <c>PlaceContext:PublicBaseUrl</c> to pin a canonical origin when running behind a proxy.
/// </summary>
public static class PublicUrl
{
    /// <summary>
    /// Absolute base URL (no trailing slash) for OAuth issuer/endpoints.
    /// Prefer <c>PlaceContext:PublicBaseUrl</c> when set (canonical public origin behind a proxy),
    /// otherwise derive from the incoming request host.
    /// </summary>
    public static string Base(HttpContext ctx, IConfiguration config)
    {
        var configured = config["PlaceContext:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim().TrimEnd('/');

        return $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    }
}
