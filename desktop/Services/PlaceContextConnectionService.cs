using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlaceContext.Desktop.Models;

namespace PlaceContext.Desktop.Services;

public sealed class OAuthConnection
{
    internal OAuthConnection(
        Uri endpoint,
        string clientId,
        string accessToken,
        string refreshToken,
        DateTimeOffset expiresAt,
        DesktopHealthResponse health,
        long latencyMilliseconds)
    {
        Endpoint = endpoint;
        ClientId = clientId;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresAt = expiresAt;
        Health = health;
        LatencyMilliseconds = latencyMilliseconds;
    }

    public Uri Endpoint { get; }
    public string ClientId { get; }
    public DesktopHealthResponse Health { get; }
    public long LatencyMilliseconds { get; }
    internal string AccessToken { get; set; }
    internal string RefreshToken { get; set; }
    internal DateTimeOffset ExpiresAt { get; set; }
    internal SemaphoreSlim RefreshLock { get; } = new(1, 1);
}

public sealed class PlaceContextConnectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly Action<Uri> _openBrowser;

    public PlaceContextConnectionService(HttpClient? httpClient = null, Action<Uri>? openBrowser = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _openBrowser = openBrowser ?? OpenBrowser;
    }

    public async Task<OAuthConnection> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var baseUri = EndpointAddress.Parse(endpoint);
        var metadata = await GetAsync<OAuthServerMetadata>(
            new Uri(baseUri, ".well-known/oauth-authorization-server"),
            cancellationToken);
        if (!metadata.ScopesSupported.Contains("desktop", StringComparer.Ordinal))
        {
            throw new HttpRequestException(
                "This PlaceContext host does not support the desktop API yet. Update the host and try again.");
        }

        await using var callback = new LoopbackOAuthReceiver();

        var registration = await PostJsonAsync<OAuthRegistrationResponse>(
            new Uri(baseUri, "connect/register"),
            new OAuthRegistrationRequest([callback.RedirectUri.ToString()], "PlaceContext Desktop"),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(registration.ClientId))
            throw new HttpRequestException("OAuth client registration did not return a client ID.");

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var authorizeUri = BuildAuthorizeUri(baseUri, registration.ClientId, callback.RedirectUri, challenge, state);

        try
        {
            _openBrowser(authorizeUri);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new HttpRequestException("The system browser could not be opened for OAuth sign-in.", exception);
        }
        using var callbackTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        callbackTimeout.CancelAfter(TimeSpan.FromMinutes(5));
        var authorization = await callback.WaitForAuthorizationAsync(callbackTimeout.Token);

        if (!string.IsNullOrWhiteSpace(authorization.Error))
            throw new HttpRequestException($"OAuth authorization failed: {authorization.Error}.");
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state),
                Encoding.UTF8.GetBytes(authorization.State ?? string.Empty)))
            throw new HttpRequestException("OAuth authorization returned an invalid state value.");
        if (string.IsNullOrWhiteSpace(authorization.Code))
            throw new HttpRequestException("OAuth authorization did not return a code.");

        var token = await ExchangeCodeAsync(
            baseUri,
            registration.ClientId,
            callback.RedirectUri,
            authorization.Code,
            verifier,
            cancellationToken);
        ValidateToken(token);

        var watch = Stopwatch.StartNew();
        var health = await GetAsync<DesktopHealthResponse>(
            baseUri,
            "api/desktop/health",
            token.AccessToken,
            cancellationToken);
        watch.Stop();

        if (!health.Ok || !string.Equals(health.Api, "desktop", StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException("The endpoint responded, but its desktop API is not healthy.");
        if (!health.Tenant.Resolved)
            throw new HttpRequestException("The desktop API did not resolve a PlaceContext workspace.");

        return new OAuthConnection(
            baseUri,
            registration.ClientId,
            token.AccessToken,
            token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn),
            health,
            watch.ElapsedMilliseconds);
    }

    public async Task<WorkspaceSnapshot> LoadWorkspaceAsync(
        OAuthConnection connection,
        CancellationToken cancellationToken = default)
    {
        await RefreshIfNeededAsync(connection, cancellationToken);
        var projects = await GetAsync<List<CoreProject>>(
            connection.Endpoint,
            "api/desktop/v1/projects",
            connection.AccessToken,
            cancellationToken);

        var jobsByProject = await Task.WhenAll(projects.Select(project =>
            GetAsync<List<CoreJob>>(
                connection.Endpoint,
                $"api/desktop/v1/projects/{project.Id:D}/jobs",
                connection.AccessToken,
                cancellationToken)));
        var jobs = jobsByProject.SelectMany(value => value).ToList();

        var runsByJob = await Task.WhenAll(jobs.Select(job =>
            GetAsync<List<CoreJobRun>>(
                connection.Endpoint,
                $"api/desktop/v1/projects/{job.ProjectId:D}/jobs/{job.Id:D}/runs?take=10",
                connection.AccessToken,
                cancellationToken)));
        var runs = runsByJob
            .SelectMany(value => value)
            .OrderByDescending(run => run.StartedAt)
            .ToList();

        var projectResources = await Task.WhenAll(projects.Select(async project =>
        {
            var tests = GetOptionalAsync<CoreResourceItem>(connection, $"api/desktop/v1/projects/{project.Id:D}/tests", cancellationToken);
            var chains = GetOptionalAsync<CoreResourceItem>(connection, $"api/desktop/v1/projects/{project.Id:D}/chains", cancellationToken);
            var schedules = GetOptionalAsync<CoreResourceItem>(connection, $"api/desktop/v1/projects/{project.Id:D}/schedules", cancellationToken);
            var data = GetOptionalAsync<CoreResourceItem>(connection, $"api/desktop/v1/projects/{project.Id:D}/data-resources", cancellationToken);
            var secrets = GetOptionalAsync<CoreResourceItem>(connection, $"api/desktop/v1/projects/{project.Id:D}/secrets", cancellationToken);
            var agents = GetOptionalAsync<CoreResourceItem>(connection, $"api/desktop/v1/projects/{project.Id:D}/agents", cancellationToken);
            var chats = GetOptionalAsync<CoreResourceItem>(connection, $"api/desktop/v1/projects/{project.Id:D}/agent-chats", cancellationToken);
            var artifacts = GetOptionalAsync<CoreResourceItem>(connection, $"api/desktop/v1/projects/{project.Id:D}/artifacts?take=200", cancellationToken);
            await Task.WhenAll(tests, chains, schedules, data, secrets, agents, chats, artifacts);
            return new ProjectResources(
                await tests, await chains, await schedules, await data,
                await secrets, await agents, await chats, await artifacts);
        }));

        var observabilityTask = GetOptionalAsync<CoreResourceItem>(
            connection, "api/desktop/v1/observability?take=50", cancellationToken);
        var clusterTask = GetOptionalAsync<CoreResourceItem>(
            connection, "api/desktop/v1/cluster", cancellationToken);
        var wikiTask = GetOptionalAsync<CoreResourceItem>(
            connection, "api/desktop/v1/wiki", cancellationToken);
        await Task.WhenAll(observabilityTask, clusterTask, wikiTask);

        return new WorkspaceSnapshot(
            projects,
            jobs,
            runs,
            projectResources.SelectMany(value => value.Tests).ToList(),
            projectResources.SelectMany(value => value.Chains).ToList(),
            projectResources.SelectMany(value => value.Schedules).ToList(),
            projectResources.SelectMany(value => value.Data).ToList(),
            projectResources.SelectMany(value => value.Secrets).ToList(),
            projectResources.SelectMany(value => value.Agents).ToList(),
            projectResources.SelectMany(value => value.Chats).ToList(),
            projectResources.SelectMany(value => value.Artifacts).ToList(),
            await observabilityTask,
            await clusterTask,
            await wikiTask);
    }

    public async Task<DesktopActionResponse> RunJobAsync(
        OAuthConnection connection,
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken = default) =>
        await PostAsync<DesktopActionResponse>(
            connection,
            $"api/desktop/v1/projects/{projectId:D}/jobs/{jobId:D}/run",
            new { inputPayload = (string?)null },
            cancellationToken);

    public async Task<DesktopActionResponse> RunTestAsync(
        OAuthConnection connection,
        Guid projectId,
        Guid testId,
        CancellationToken cancellationToken = default) =>
        await PostAsync<DesktopActionResponse>(
            connection,
            $"api/desktop/v1/projects/{projectId:D}/tests/{testId:D}/run",
            new { },
            cancellationToken);

    public async Task<DesktopActionResponse> RunChainAsync(
        OAuthConnection connection,
        Guid projectId,
        Guid chainId,
        CancellationToken cancellationToken = default) =>
        await PostAsync<DesktopActionResponse>(
            connection,
            $"api/desktop/v1/projects/{projectId:D}/chains/{chainId:D}/run",
            new { inputPayload = (string?)null },
            cancellationToken);

    public async Task<DesktopActionResponse> SetScheduleEnabledAsync(
        OAuthConnection connection,
        Guid projectId,
        Guid scheduleId,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        await PostAsync<DesktopActionResponse>(
            connection,
            $"api/desktop/v1/projects/{projectId:D}/schedules/{scheduleId:D}/enabled",
            new { enabled },
            cancellationToken);

    public async Task<DesktopQueryResponse> QueryDataAsync(
        OAuthConnection connection,
        Guid projectId,
        string sql,
        CancellationToken cancellationToken = default) =>
        await PostAsync<DesktopQueryResponse>(
            connection,
            $"api/desktop/v1/projects/{projectId:D}/data/query",
            new { sql },
            cancellationToken);

    public async Task<DesktopChatSession> GetAgentChatAsync(
        OAuthConnection connection,
        Guid projectId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await RefreshIfNeededAsync(connection, cancellationToken);
        return await GetAsync<DesktopChatSession>(
            connection.Endpoint,
            $"api/desktop/v1/projects/{projectId:D}/agent-chats/{sessionId:D}",
            connection.AccessToken,
            cancellationToken);
    }

    public async Task<DesktopChatSession> SendAgentMessageAsync(
        OAuthConnection connection,
        Guid projectId,
        Guid? sessionId,
        string message,
        CancellationToken cancellationToken = default) =>
        await PostAsync<DesktopChatSession>(
            connection,
            $"api/desktop/v1/projects/{projectId:D}/agent-chats/messages",
            new { sessionId, message },
            cancellationToken);

    private async Task<List<T>> GetOptionalAsync<T>(
        OAuthConnection connection,
        string relativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetAsync<List<T>>(
                connection.Endpoint,
                relativePath,
                connection.AccessToken,
                cancellationToken);
        }
        catch (HttpRequestException exception) when (
            exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return [];
        }
    }

    private async Task RefreshIfNeededAsync(OAuthConnection connection, CancellationToken cancellationToken)
    {
        if (connection.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return;

        await connection.RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (connection.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return;
            using var response = await _httpClient.PostAsync(
                new Uri(connection.Endpoint, "connect/token"),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = connection.RefreshToken,
                    ["client_id"] = connection.ClientId,
                }),
                cancellationToken);
            var token = await ReadResponseAsync<OAuthTokenResponse>(response, cancellationToken);
            ValidateToken(token);
            connection.AccessToken = token.AccessToken;
            connection.RefreshToken = token.RefreshToken;
            connection.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
        }
        finally
        {
            connection.RefreshLock.Release();
        }
    }

    private async Task<OAuthTokenResponse> ExchangeCodeAsync(
        Uri endpoint,
        string clientId,
        Uri redirectUri,
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(
            new Uri(endpoint, "connect/token"),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri.ToString(),
                ["client_id"] = clientId,
                ["code_verifier"] = verifier,
            }),
            cancellationToken);
        return await ReadResponseAsync<OAuthTokenResponse>(response, cancellationToken);
    }

    private async Task<T> PostJsonAsync<T>(Uri uri, object body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(uri, body, JsonOptions, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> GetAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> GetAsync<T>(Uri endpoint, string relativePath, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostAsync<T>(
        OAuthConnection connection,
        string relativePath,
        object body,
        CancellationToken cancellationToken)
    {
        await RefreshIfNeededAsync(connection, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(connection.Endpoint, relativePath))
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body;
            var route = response.RequestMessage?.RequestUri?.AbsolutePath;
            var location = string.IsNullOrWhiteSpace(route) ? string.Empty : $" from {route}";
            throw new HttpRequestException(
                $"PlaceContext returned {(int)response.StatusCode}{location}: {detail}",
                null,
                response.StatusCode);
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                ?? throw new HttpRequestException("PlaceContext returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("PlaceContext returned data in an unexpected format.", exception);
        }
    }

    private static Uri BuildAuthorizeUri(
        Uri endpoint,
        string clientId,
        Uri redirectUri,
        string challenge,
        string state)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["response_type"] = "code",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = "desktop",
            ["state"] = state,
        };
        var query = string.Join("&", values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri(new Uri(endpoint, "connect/authorize"), $"?{query}");
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void ValidateToken(OAuthTokenResponse token)
    {
        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new HttpRequestException("OAuth token exchange returned an incomplete token response.");
    }

    private static void OpenBrowser(Uri uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true,
        });
    }

    private sealed record OAuthRegistrationRequest(
        [property: JsonPropertyName("redirect_uris")] string[] RedirectUris,
        [property: JsonPropertyName("client_name")] string ClientName);
    private sealed record OAuthRegistrationResponse(
        [property: JsonPropertyName("client_id")] string ClientId);
    private sealed record OAuthServerMetadata(
        [property: JsonPropertyName("scopes_supported")] string[] ScopesSupported);
    private sealed record OAuthTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] long ExpiresIn,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("scope")] string Scope);

    private sealed class LoopbackOAuthReceiver : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public LoopbackOAuthReceiver()
        {
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            RedirectUri = new Uri($"http://127.0.0.1:{port}/callback");
        }

        public Uri RedirectUri { get; }

        public async Task<OAuthCallback> WaitForAuthorizationAsync(CancellationToken cancellationToken)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(requestLine))
                throw new HttpRequestException("OAuth callback did not contain an HTTP request.");

            string? header;
            do
            {
                header = await reader.ReadLineAsync(cancellationToken);
            } while (!string.IsNullOrEmpty(header));

            var target = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1)
                ?? throw new HttpRequestException("OAuth callback request was malformed.");
            var callbackUri = new Uri(RedirectUri, target);
            var values = ParseQuery(callbackUri.Query);

            const string html = "<!doctype html><title>PlaceContext</title><h1>Signed in</h1><p>You can close this window and return to PlaceContext Desktop.</p>";
            var body = Encoding.UTF8.GetBytes(html);
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);

            values.TryGetValue("code", out var code);
            values.TryGetValue("state", out var state);
            values.TryGetValue("error", out var error);
            return new OAuthCallback(code, state, error);
        }

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                values[Uri.UnescapeDataString(pair[0].Replace('+', ' '))] =
                    pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty;
            }
            return values;
        }
    }

    private sealed record OAuthCallback(string? Code, string? State, string? Error);
    private sealed record ProjectResources(
        IReadOnlyList<CoreResourceItem> Tests,
        IReadOnlyList<CoreResourceItem> Chains,
        IReadOnlyList<CoreResourceItem> Schedules,
        IReadOnlyList<CoreResourceItem> Data,
        IReadOnlyList<CoreResourceItem> Secrets,
        IReadOnlyList<CoreResourceItem> Agents,
        IReadOnlyList<CoreResourceItem> Chats,
        IReadOnlyList<CoreResourceItem> Artifacts);
}
