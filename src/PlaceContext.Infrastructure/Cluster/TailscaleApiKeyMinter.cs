using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Cluster;

/// <summary>
/// Real Tailscale API-backed <see cref="ITailscaleKeyMinter"/>: exchanges an OAuth client
/// (client_id/client_secret, minted in the Tailscale admin console with the "Devices Core: Write"
/// scope) for a short-lived access token, then requests a single-use, ephemeral, pre-authorized
/// device key so a brand-new agent machine can join the tailnet unattended.
/// </summary>
public sealed class TailscaleApiKeyMinter : ITailscaleKeyMinter
{
    private const string TokenEndpoint = "https://api.tailscale.com/api/v2/oauth/token";
    private const string KeysEndpoint = "https://api.tailscale.com/api/v2/tailnet/-/keys";
    private const string DefaultTag = "tag:agent";
    private const int ExpirySeconds = 600;

    private readonly IHttpClientFactory _http;

    public TailscaleApiKeyMinter(IHttpClientFactory http) => _http = http;

    public async Task<string?> MintEphemeralAgentKeyAsync(string clientId, string clientSecret, string tags, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return null;

        try
        {
            var client = _http.CreateClient();

            using var tokenReq = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                }),
            };
            using var tokenResp = await client.SendAsync(tokenReq, ct);
            if (!tokenResp.IsSuccessStatusCode) return null;

            using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync(ct));
            if (!tokenDoc.RootElement.TryGetProperty("access_token", out var tokenEl)) return null;
            var accessToken = tokenEl.GetString();
            if (string.IsNullOrWhiteSpace(accessToken)) return null;

            var tagList = (tags ?? "")
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tagList.Length == 0) tagList = new[] { DefaultTag };

            using var keysReq = new HttpRequestMessage(HttpMethod.Post, KeysEndpoint)
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) },
                Content = JsonContent.Create(new
                {
                    capabilities = new
                    {
                        devices = new
                        {
                            create = new
                            {
                                reusable = false,
                                ephemeral = true,
                                preauthorized = true,
                                tags = tagList,
                            },
                        },
                    },
                    expirySeconds = ExpirySeconds,
                    description = "placecontext agent join",
                }),
            };
            using var keysResp = await client.SendAsync(keysReq, ct);
            if (!keysResp.IsSuccessStatusCode) return null;

            using var keysDoc = JsonDocument.Parse(await keysResp.Content.ReadAsStringAsync(ct));
            if (!keysDoc.RootElement.TryGetProperty("key", out var keyEl)) return null;
            return keyEl.GetString();
        }
        catch
        {
            return null;
        }
    }
}
