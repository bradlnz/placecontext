namespace PlaceContext.Desktop.Services;

public static class EndpointAddress
{
    public static Uri Parse(string value)
    {
        var candidate = value.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            throw new ArgumentException("Enter your PlaceContext endpoint.");

        if (!candidate.Contains("://", StringComparison.Ordinal))
            candidate = IsLocal(candidate) ? $"http://{candidate}" : $"https://{candidate}";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Use an http:// or https:// endpoint.");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Credentials are not allowed in the endpoint URL.");

        return new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
            Path = uri.AbsolutePath.TrimEnd('/') + "/",
        }.Uri;
    }

    private static bool IsLocal(string value) =>
        value.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("127.", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("10.", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("192.168.", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
}
