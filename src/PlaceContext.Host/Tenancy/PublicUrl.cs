namespace PlaceContext.Host.Tenancy;

/// <summary>
/// Builds public absolute URLs for OAuth metadata and challenges. Each deployment is
/// client-isolated on the client's own hardware, so the request <c>Host</c> is trusted directly;
/// set <c>PlaceContext:PublicBaseUrl</c> to pin a canonical origin when running behind a proxy.
/// </summary>
public static class PublicUrl
{
    /// <summary>Base domains whose subdomains identify tenants (product apex + dev loopback).</summary>
    internal static readonly string[] TenantBaseDomains = [".placecontext.ai", ".localhost"];

    /// <summary>Exact hosts served as the shared default tenant (loopback addresses + apex).</summary>
    internal static readonly string[] DefaultTenantHosts = ["localhost", "placecontext.ai", "127.0.0.1", "::1", "[::1]"];

    /// <summary>
    /// True when <paramref name="host"/> is one of ours: the apex, a tenant subdomain of a known
    /// base domain, or a dev loopback address. Anything else (e.g. a spoofed Host header) is
    /// untrusted. Fails closed on an empty host.
    /// </summary>
    public static bool IsTrustedHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        host = host.Trim().ToLowerInvariant();
        if (DefaultTenantHosts.Contains(host, StringComparer.Ordinal))
            return true;

        foreach (var baseDomain in TenantBaseDomains)
            if (host.EndsWith(baseDomain, StringComparison.Ordinal))
                return true;

        return false;
    }

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
