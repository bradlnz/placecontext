using System.Collections.Concurrent;
using System.Net;
using System.Text;
using PlaceContext.Desktop.Services;

namespace PlaceContext.Desktop.Tests;

public sealed class PlaceContextConnectionServiceTests
{
    [Fact]
    public async Task ConnectAsync_completes_oauth_pkce_and_uses_desktop_health_endpoint()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith("/.well-known/oauth-authorization-server", StringComparison.Ordinal) => Metadata("desktop", "mcp", "identity"),
            var path when path.EndsWith("/connect/register", StringComparison.Ordinal) => Json("""{"client_id":"pc_desktop"}"""),
            var path when path.EndsWith("/connect/token", StringComparison.Ordinal) => Json("""{"access_token":"access-token","refresh_token":"refresh-token","expires_in":3600,"token_type":"Bearer","scope":"desktop"}"""),
            var path when path.EndsWith("/api/desktop/health", StringComparison.Ordinal) => Json("""
                {"ok":true,"api":"desktop","tenant":{"resolved":true,"id":"11111111-1111-1111-1111-111111111111","slug":"acme"},"userId":"22222222-2222-2222-2222-222222222222","role":"Owner","issuedAt":"2026-08-25T00:00:00Z"}
                """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        Uri? authorizationUri = null;
        var service = new PlaceContextConnectionService(
            new HttpClient(handler),
            uri =>
            {
                authorizationUri = uri;
                CompleteAuthorizationAsync(uri, "code-123");
            });

        var result = await service.ConnectAsync("https://example.com/placecontext");

        Assert.Equal("acme", result.Health.Tenant.Slug);
        Assert.NotNull(authorizationUri);
        Assert.Equal("desktop", Query(authorizationUri!, "scope"));
        Assert.Equal("S256", Query(authorizationUri!, "code_challenge_method"));
        Assert.StartsWith("http://127.0.0.1:", Query(authorizationUri!, "redirect_uri"));
        var registrationRequest = Assert.Single(handler.Requests, request => request.Uri.AbsolutePath.EndsWith("/connect/register", StringComparison.Ordinal));
        Assert.Contains("redirect_uris", registrationRequest.Body);
        Assert.Contains("client_name", registrationRequest.Body);
        var healthRequest = Assert.Single(handler.Requests, request => request.Uri.AbsolutePath == "/placecontext/api/desktop/health");
        Assert.Equal("Bearer access-token", healthRequest.Authorization);
    }

    [Fact]
    public async Task ConnectAsync_rejects_callback_with_wrong_state()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/.well-known/oauth-authorization-server" => Metadata("desktop", "mcp", "identity"),
            "/connect/register" => Json("""{"client_id":"pc_desktop"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var service = new PlaceContextConnectionService(
            new HttpClient(handler),
            uri => CompleteAuthorizationAsync(uri, "code-123", "wrong-state"));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.ConnectAsync("https://example.com"));

        Assert.Contains("invalid state", error.Message);
    }

    [Fact]
    public async Task ConnectAsync_rejects_host_without_desktop_scope_before_opening_browser()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/.well-known/oauth-authorization-server" => Metadata("mcp", "identity"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var browserOpened = false;
        var service = new PlaceContextConnectionService(
            new HttpClient(handler),
            _ => browserOpened = true);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.ConnectAsync("https://example.com"));

        Assert.Contains("does not support the desktop API", error.Message);
        Assert.False(browserOpened);
        Assert.DoesNotContain(handler.Requests, request =>
            request.Uri.AbsolutePath == "/connect/register");
    }

    [Fact]
    public async Task LoadWorkspaceAsync_uses_oauth_bearer_and_desktop_routes()
    {
        var projectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var jobId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/.well-known/oauth-authorization-server" => Metadata("desktop", "mcp", "identity"),
            "/connect/register" => Json("""{"client_id":"pc_desktop"}"""),
            "/connect/token" => Json("""{"access_token":"access-token","refresh_token":"refresh-token","expires_in":3600,"token_type":"Bearer","scope":"desktop"}"""),
            "/api/desktop/health" => Json("""
                {"ok":true,"api":"desktop","tenant":{"resolved":true,"id":"11111111-1111-1111-1111-111111111111","slug":"acme"},"userId":"22222222-2222-2222-2222-222222222222","role":"Owner","issuedAt":"2026-08-25T00:00:00Z"}
                """),
            "/api/desktop/v1/projects" => Json($$"""
                [{"id":"{{projectId}}","name":"Production","path":"/srv/production","status":"Ready","isGraphified":true}]
                """),
            var path when path.EndsWith("/jobs", StringComparison.Ordinal) => Json($$"""
                [{"id":"{{jobId}}","projectId":"{{projectId}}","name":"Deploy","description":"Release production","mapSourceKind":"Code","returnType":"Text","allowApiInvocation":true,"allowNetworkEgress":false,"updatedAt":"2026-08-25T00:00:00Z"}]
                """),
            var path when path.EndsWith("/runs", StringComparison.Ordinal) => Json($$"""
                [{"id":"33333333-3333-3333-3333-333333333333","jobId":"{{jobId}}","status":"Succeeded","startedAt":"2026-08-25T00:00:00Z","finishedAt":"2026-08-25T00:01:00Z","shardCount":1,"succeededShards":1,"partialShards":0,"failedShards":0}]
                """),
            var path when path.EndsWith("/tests", StringComparison.Ordinal) => Json($$"""
                [{"id":"44444444-4444-4444-4444-444444444444","projectId":"{{projectId}}","kind":"test","title":"Smoke test","detail":"Deploy","meta":"Succeeded","status":"Passed"}]
                """),
            "/api/desktop/v1/cluster" => Json("""[{"id":null,"projectId":null,"kind":"node","title":"worker-1","detail":"Linux · x64","meta":"worker","status":"Ready"}]"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var service = new PlaceContextConnectionService(
            new HttpClient(handler),
            uri => CompleteAuthorizationAsync(uri, "code-123"));
        var connection = await service.ConnectAsync("https://example.com");

        var snapshot = await service.LoadWorkspaceAsync(connection);

        Assert.Single(snapshot.Projects);
        Assert.Equal("Deploy", Assert.Single(snapshot.Jobs).Name);
        Assert.Equal("Succeeded", Assert.Single(snapshot.Runs).Status);
        Assert.Equal("Smoke test", Assert.Single(snapshot.Tests).Title);
        Assert.Equal("worker-1", Assert.Single(snapshot.Cluster).Title);
        var apiRequests = handler.Requests.Where(request => request.Uri.AbsolutePath.StartsWith("/api/desktop", StringComparison.Ordinal));
        Assert.All(apiRequests, request => Assert.Equal("Bearer access-token", request.Authorization));
    }

    [Fact]
    public async Task Native_actions_use_desktop_routes_and_oauth_bearer()
    {
        var projectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var resourceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/.well-known/oauth-authorization-server" => Metadata("desktop"),
            "/connect/register" => Json("""{"client_id":"pc_desktop"}"""),
            "/connect/token" => Json("""{"access_token":"access-token","refresh_token":"refresh-token","expires_in":3600,"token_type":"Bearer","scope":"desktop"}"""),
            "/api/desktop/health" => Json("""
                {"ok":true,"api":"desktop","tenant":{"resolved":true,"id":"11111111-1111-1111-1111-111111111111","slug":"acme"},"userId":"22222222-2222-2222-2222-222222222222","role":"Owner","issuedAt":"2026-08-25T00:00:00Z"}
                """),
            var path when path.EndsWith("/run", StringComparison.Ordinal) => Json("""{"status":"Queued","message":"Run started.","runId":"33333333-3333-3333-3333-333333333333"}"""),
            var path when path.EndsWith("/enabled", StringComparison.Ordinal) => Json("""{"status":"Disabled","message":"Schedule disabled.","runId":null}"""),
            var path when path.EndsWith("/data/query", StringComparison.Ordinal) => Json("""{"columns":["name"],"rows":[["Ada"]],"affectedRows":0,"truncated":false}"""),
            var path when path.EndsWith("/agent-chats/messages", StringComparison.Ordinal) => Json($$"""{"id":"{{resourceId}}","projectId":"{{projectId}}","title":"Native chat","messages":[{"role":"assistant","content":"Hello","timestamp":"2026-08-25T00:00:00Z"}],"updatedAt":"2026-08-25T00:00:00Z"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var service = new PlaceContextConnectionService(new HttpClient(handler), uri => CompleteAuthorizationAsync(uri, "code-123"));
        var connection = await service.ConnectAsync("https://example.com");

        Assert.Equal("Queued", (await service.RunJobAsync(connection, projectId, resourceId)).Status);
        Assert.Equal("Queued", (await service.RunTestAsync(connection, projectId, resourceId)).Status);
        Assert.Equal("Queued", (await service.RunChainAsync(connection, projectId, resourceId)).Status);
        Assert.Equal("Disabled", (await service.SetScheduleEnabledAsync(connection, projectId, resourceId, false)).Status);
        Assert.Equal("Ada", (await service.QueryDataAsync(connection, projectId, "SELECT name FROM people")).Rows[0][0]);
        Assert.Equal("Hello", (await service.SendAgentMessageAsync(connection, projectId, null, "Hi")).Messages[0].Content);

        var actionRequests = handler.Requests.Where(request => request.Uri.AbsolutePath.StartsWith("/api/desktop/v1", StringComparison.Ordinal));
        Assert.All(actionRequests, request => Assert.Equal("Bearer access-token", request.Authorization));
    }

    private static void CompleteAuthorizationAsync(Uri authorizationUri, string code, string? state = null)
    {
        _ = Task.Run(async () =>
        {
            var redirect = Query(authorizationUri, "redirect_uri");
            var returnedState = state ?? Query(authorizationUri, "state");
            using var callbackClient = new HttpClient();
            await callbackClient.GetAsync(
                $"{redirect}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(returnedState)}");
        });
    }

    private static string Query(Uri uri, string key)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (Uri.UnescapeDataString(pair[0]) == key)
                return pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
        }
        throw new InvalidOperationException($"Query parameter '{key}' was not found.");
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Metadata(params string[] scopes) =>
        Json($$"""{"scopes_supported":{{System.Text.Json.JsonSerializer.Serialize(scopes)}}}""");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public ConcurrentBag<RequestRecord> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RequestRecord(
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                body));
            return response(request);
        }
    }

    private sealed record RequestRecord(Uri Uri, string? Authorization, string? Body);
}
