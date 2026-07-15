namespace PlaceContext.Host.Tenancy;

/// <summary>
/// Builds public absolute URLs for OAuth metadata and challenges. Refuses to honor arbitrary
/// <c>Host</c> headers (Host-header injection would otherwise poison issuer/token endpoints and
/// auto-mint tenants from attacker-controlled names).
/// </summary>
public static class PublicUrl
{
    /// <summary>
    /// True when <paramref name="host"/> is a known local/dev or product base (with optional port stripped).
    /// </summary>
    public static bool IsTrustedHost(string? host)
    {
        host = (host ?? string.Empty).ToLowerInvariant();
        if (host.Length == 0) return false;
        // Host header never includes port in HttpContext.Request.Host.Host
        if (host is "localhost" or "127.0.0.1" or "::1" or "[::1]" or "placecontext.ai")
            return true;
        if (host.EndsWith(".localhost", StringComparison.Ordinal)) return true;
        if (host.EndsWith(".placecontext.ai", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// Absolute base URL (no trailing slash) for OAuth issuer/endpoints.
    /// Prefer <c>PlaceContext:PublicBaseUrl</c> when set (canonical public origin behind a proxy).
    /// </summary>
    public static string Base(HttpContext ctx, IConfiguration config)
    {
        var configured = config["PlaceContext:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim().TrimEnd('/');

        var host = ctx.Request.Host.Host;
        if (IsTrustedHost(host))
            return $"{ctx.Request.Scheme}://{ctx.Request.Host}";

        // Host-header injection / unknown host: pin OAuth URLs to loopback so metadata cannot
        // advertise attacker-controlled issuer/token endpoints. Use the real listen port (the
        // injected Host header often omits it).
        var port = ctx.Connection.LocalPort;
        var authority = port is 0 or 80 or 443 ? "127.0.0.1" : $"127.0.0.1:{port}";
        return $"{ctx.Request.Scheme}://{authority}";
    }
}
