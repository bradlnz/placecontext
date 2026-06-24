using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Options;

namespace PlaceContext.Infrastructure.GitHub;

/// <summary>
/// GitHub OAuth + REST gateway. Builds the authorize URL, exchanges the callback code for an access
/// token, and lists the connected account's repositories. Credentials come from
/// <see cref="GitHubOptions"/>; when unset, <see cref="IsConfigured"/> is false and the portal shows
/// setup guidance instead of attempting the flow.
/// </summary>
public sealed class GitHubGateway : IGitHubGateway
{
    private const string Authorize = "https://github.com/login/oauth/authorize";
    private const string Token = "https://github.com/login/oauth/access_token";
    private const string Api = "https://api.github.com";

    private readonly IHttpClientFactory _http;
    private readonly GitHubOptions _options;

    public GitHubGateway(IHttpClientFactory http, IOptions<PlaceContextOptions> options)
    {
        _http = http;
        _options = options.Value.GitHub;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ClientId) && !string.IsNullOrWhiteSpace(_options.ClientSecret);

    public string BuildAuthorizeUrl(string redirectUri, string state)
    {
        var q = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "repo read:user",
            ["state"] = state,
        };
        var query = string.Join("&", q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}"));
        return $"{Authorize}?{query}";
    }

    public async Task<string?> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        using var client = _http.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await client.PostAsJsonAsync(Token, new
        {
            client_id = _options.ClientId,
            client_secret = _options.ClientSecret,
            code,
            redirect_uri = redirectUri,
        }, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var payload = await resp.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        return string.IsNullOrWhiteSpace(payload?.AccessToken) ? null : payload!.AccessToken;
    }

    public async Task<(string Login, string Name)?> GetUserAsync(string accessToken, CancellationToken ct = default)
    {
        using var client = ApiClient(accessToken);
        var user = await client.GetFromJsonAsync<UserResponse>($"{Api}/user", ct);
        return user is null ? null : (user.Login, user.Name ?? user.Login);
    }

    public async Task<IReadOnlyList<GitHubRepo>> ListReposAsync(string accessToken, CancellationToken ct = default)
    {
        using var client = ApiClient(accessToken);
        var repos = await client.GetFromJsonAsync<List<RepoResponse>>(
            $"{Api}/user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member", ct);
        return (repos ?? new())
            .Select(r => new GitHubRepo(r.FullName, r.Name, r.CloneUrl, r.Private, r.Description, r.Language, r.PushedAt))
            .ToList();
    }

    private HttpClient ApiClient(string accessToken)
    {
        var client = _http.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PlaceContext", "1.0"));
        return client;
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);
    private sealed record UserResponse(
        [property: JsonPropertyName("login")] string Login,
        [property: JsonPropertyName("name")] string? Name);
    private sealed record RepoResponse(
        [property: JsonPropertyName("full_name")] string FullName,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("clone_url")] string CloneUrl,
        [property: JsonPropertyName("private")] bool Private,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt);
}
