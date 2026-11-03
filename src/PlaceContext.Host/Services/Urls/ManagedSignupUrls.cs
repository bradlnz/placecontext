using PlaceContext.Application.Auth;
using PlaceContext.Application.Billing;

namespace PlaceContext.Host.Tenancy;

/// <summary>Canonical signup origin and tenant URLs. Never create destinations from a
/// caller-controlled Host header, forwarded host or arbitrary returnUrl.</summary>
public static class ManagedSignupUrls
{
    public static Uri? PublicOrigin(IConfiguration configuration)
    {
        if (!Uri.TryCreate(configuration["PlaceContext:PublicBaseUrl"],
                UriKind.Absolute, out var parsed)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment)
            || parsed.AbsolutePath != "/"
            || parsed.Host is not ("placecontext.io" or "localhost" or "127.0.0.1")
            || parsed.Scheme != Uri.UriSchemeHttps && !(parsed.IsLoopback && parsed.Scheme == Uri.UriSchemeHttp))
        {
            return null;
        }
        return parsed;
    }

    public static bool IsApexHost(string? host, IConfiguration config)
    {
        var origin = PublicOrigin(config);
        return origin is not null
            && string.Equals(host, origin.Host, StringComparison.OrdinalIgnoreCase);
    }

    public static Uri TenantUri(IConfiguration config, string slug, string path = "/")
    {
        if (ManagedWorkspaceSlugInvalid(slug))
        {
            throw new ArgumentException("Invalid workspace address.", nameof(slug));
        }
        var origin = PublicOrigin(config)
                    ?? throw new InvalidOperationException("Canonical PlaceContext signup origin is not configured.");
        var uri = new UriBuilder(origin)
        {
            Host = $"{slug}.{origin.Host}",
            Path = path,
            Query = "",
            Fragment = ""
        };
        return uri.Uri;
    }

    public static Uri SignupUri(IConfiguration config) =>
        new(PublicOrigin(config) ?? throw new InvalidOperationException("Signup origin missing."), "/signup");

    private static bool ManagedWorkspaceSlugInvalid(string slug) =>
        PlaceContext.Application.Auth.WorkspaceSignupValidation.ValidateSlug(slug) is not null;

    // Public UI infrastructure routes are allowed only on the canonical apex.
    // They expose no workspace data: resource assets are served earlier by
    // static-file middleware, while the Blazor circuit still enforces per-page
    // authorization. Tenant APIs and signup completion remain tenant-scoped.
    public static bool IsPublicApexPath(PathString path) =>
        path.Equals("/healthz", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/_blazor")
        || path.StartsWithSegments("/signup")
        || path.Equals("/login", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/locked", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/auth/workspace", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/auth/signup", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/auth/signup/verify", StringComparison.OrdinalIgnoreCase);
}
