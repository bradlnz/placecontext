using System.Text.Json;
using System.Text.RegularExpressions;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Auth;

internal static class ChatGptClientMetadata
{
    // Only this operator-approved origin may supply remote clients. Never fetch arbitrary client_id
    // URLs: authorization auto-consents after login and the server can reach private infrastructure.
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    }) { Timeout = TimeSpan.FromSeconds(10) };

    internal static bool IsTrustedId(string id) => id.Length <= 256 && Regex.IsMatch(id,
        @"\Ahttps://chatgpt\.com/oauth/(?:[A-Za-z0-9_-]+/)?client\.json\z", RegexOptions.CultureInvariant);

    internal static async Task<OAuthClient?> ResolveAsync(string id, string redirect,
        CancellationToken ct, HttpClient? http = null)
    {
        if (!IsTrustedId(id)) return null;
        try
        {
            using var response = await (http ?? Http).GetAsync(id, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;
            // Bound both streamed and declared lengths; do not trust Content-Length alone.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await response.Content.LoadIntoBufferAsync(64 * 1024, timeout.Token);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(timeout.Token));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("client_id", out var clientId)
                || clientId.ValueKind != JsonValueKind.String || clientId.GetString() != id
                || !Contains(root, "redirect_uris", redirect)
                || !Uri.TryCreate(redirect, UriKind.Absolute, out var uri)
                || uri.Scheme != "https" || uri.Host != "chatgpt.com" || !uri.IsDefaultPort
                || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
                return null;
            // The plural field supersedes the legacy preference (ChatGPT prefers private_key_jwt,
            // but also supports the public PKCE flow that this server advertises).
            var supportsNone = root.TryGetProperty("token_endpoint_auth_methods_supported", out _)
                ? Contains(root, "token_endpoint_auth_methods_supported", "none")
                : root.TryGetProperty("token_endpoint_auth_method", out var method)
                    && method.ValueKind == JsonValueKind.String && method.GetString() == "none";
            if (!supportsNone
                || (root.TryGetProperty("grant_types", out _) && !Contains(root, "grant_types", "authorization_code"))
                || (root.TryGetProperty("response_types", out _) && !Contains(root, "response_types", "code")))
                return null;
            return new OAuthClient(id, [redirect], "ChatGPT");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool Contains(JsonElement root, string key, string value) =>
        root.TryGetProperty(key, out var values) && values.ValueKind == JsonValueKind.Array
        && values.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == value);
}
