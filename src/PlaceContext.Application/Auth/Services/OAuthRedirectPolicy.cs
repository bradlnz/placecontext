namespace PlaceContext.Application.Auth;

public static class OAuthRedirectPolicy
{
    public static bool IsSafeRedirectUri(string? uri) => IsLoopbackRedirectUri(uri);

    public static bool IsLoopbackRedirectUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u))
        {
            return false;
        }
        if (!u.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    && !u.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return u.IsLoopback
                    || string.Equals(u.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(u.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(u.Host, "[::1]", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(u.Host, "::1", StringComparison.OrdinalIgnoreCase);
    }

}
