using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlaceContext.App.Authentication;
using PlaceContext.App.Controllers;
using PlaceContext.App.Dashboard;
using PlaceContext.App.Proxy;
using PlaceContext.App.Wiki;

namespace PlaceContext.App.Tests;

public sealed class EdgeOwnedControllerTests
{
    [Fact]
    public async Task Workspace_session_projects_the_authenticated_identity_contract()
    {
        var controller = new WorkspaceController(CreateCaller(TokenHandler(new
        {
            name = "Ada Lovelace",
            role = "Owner",
            tenant_slug = "analytical-engine",
        })));
        SetContext(controller, withCookie: true);

        var result = await controller.Session();

        var response = Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Ada Lovelace", response.DisplayName);
        Assert.Equal("Owner", response.Role);
        Assert.Equal("analytical-engine", response.Tenant);
    }

    [Fact]
    public async Task Edge_owned_reads_reject_requests_without_an_identity_cookie()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException(
            "Identity must not be called without a cookie."));
        var session = new WorkspaceController(CreateCaller(handler));
        var wiki = new WikiController(CreateCaller(handler));
        SetContext(session, withCookie: false);
        SetContext(wiki, withCookie: false);

        var sessionResult = await session.Session();
        var wikiResult = await wiki.Get(null);

        Assert.IsType<UnauthorizedResult>(sessionResult.Result);
        Assert.IsType<UnauthorizedResult>(wikiResult.Result);
    }

    [Fact]
    public async Task Wiki_route_returns_the_existing_ordered_article_contract()
    {
        var controller = new WikiController(CreateCaller(TokenHandler(new
        {
            name = "Reader",
            role = "Viewer",
            tenant_slug = "docs",
        })));
        SetContext(controller, withCookie: true);

        var result = await controller.Get("getting-started");

        var response = Assert.IsType<WikiContextResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(WikiLibrary.Articles.Length, response.Articles.Count);
        Assert.Equal("getting-started", response.Articles[0].Slug);
        Assert.Equal("getting-started", response.Article?.Slug);
        Assert.Contains("What PlaceContext does", response.Article?.Html);
    }

    [Fact]
    public void App_owns_dashboard_and_complete_workspace_routes()
    {
        var assembly = typeof(WorkspaceController).Assembly;
        var dashboard = Assert.Single(assembly.GetTypes(), type => type.Name == "DashboardController");

        Assert.Equal("api/v1/dashboard", dashboard.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Contains(dashboard.GetMethods(), method => HttpTemplate(method) is null && method.Name == "Get");
        Assert.Contains(dashboard.GetMethods(), method =>
            HttpTemplate(method) == "projects/{projectId:guid}/chains/{chainId:guid}/runs"
            && method.GetCustomAttribute<HttpPostAttribute>() is not null);

        var workspaceRoutes = typeof(WorkspaceController).GetMethods()
            .Select(HttpTemplate)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            new HashSet<string>(["session", "projects", "focus", "stats"], StringComparer.Ordinal),
            workspaceRoutes);
    }

    [Fact]
    public void Dashboard_wire_contract_keeps_the_legacy_json_shape()
    {
        var projectId = Guid.NewGuid();
        var response = new DashboardResponse(
            new DashboardProject(projectId, "Project"),
            new DashboardStats(1, 2, 3, 4),
            [],
            [],
            [],
            []);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(projectId, json.RootElement.GetProperty("project").GetProperty("id").GetGuid());
        Assert.Equal(2, json.RootElement.GetProperty("stats").GetProperty("queued").GetInt32());
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("recentRuns").ValueKind);
    }

    private static string? HttpTemplate(MethodInfo method) =>
        method.GetCustomAttributes<HttpMethodAttribute>().SingleOrDefault()?.Template;

    private static EdgeCallerContext CreateCaller(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        var options = Options.Create(new MicroserviceProxyOptions
        {
            Destinations = new Dictionary<string, string>
            {
                ["Identity"] = "https://identity.internal",
            },
        });
        return new EdgeCallerContext(new EdgeServiceTokenClient(
            factory,
            options,
            NullLogger<EdgeServiceTokenClient>.Instance));
    }

    private static HttpMessageHandler TokenHandler(object claims)
    {
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var token = $"header.{payload}.signature";
        return new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    accessToken = token,
                    expiresAt = "2026-08-09T07:00:00Z",
                }),
                Encoding.UTF8,
                "application/json"),
        }));
    }

    private static void SetContext(ControllerBase controller, bool withCookie)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("portal.placecontext.test");
        if (withCookie) context.Request.Headers.Cookie = "placecontext.identity=session";
        controller.ControllerContext = new ControllerContext { HttpContext = context };
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => response(request);
    }
}
