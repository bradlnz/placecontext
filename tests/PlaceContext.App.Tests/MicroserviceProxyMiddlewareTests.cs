using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlaceContext.App.Proxy;

namespace PlaceContext.App.Tests;

public sealed class MicroserviceProxyMiddlewareTests
{
    [Fact]
    public async Task Forwards_the_request_and_streams_the_upstream_response()
    {
        HttpRequestMessage? forwarded = null;
        string? forwardedBody = null;
        var handler = new RecordingHandler(async request =>
        {
            forwarded = request;
            forwardedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync();

            var response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"id\":42}", Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("X-Upstream", "jobs");
            response.Headers.TryAddWithoutValidation("Connection", "keep-alive");
            return response;
        });
        var context = CreateContext("POST", "/api/jobs/projects/one/runs", "?wait=true");
        context.Request.Headers.Authorization = "Bearer signed-service-token";
        context.Request.Headers.Cookie = "portal=do-not-forward";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.99";
        context.Request.Headers["X-Request-Id"] = "request-42";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        var body = Encoding.UTF8.GetBytes("{\"jobId\":1}");
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Request.ContentType = "application/json";

        var middleware = CreateMiddleware(
            handler,
            new Dictionary<string, string> { ["Jobs"] = "https://jobs.internal" });

        await middleware.InvokeAsync(context);

        Assert.NotNull(forwarded);
        Assert.Equal(HttpMethod.Post, forwarded.Method);
        Assert.Equal(
            "https://jobs.internal/api/jobs/projects/one/runs?wait=true",
            forwarded.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", forwarded.Headers.Authorization?.Scheme);
        Assert.Equal("signed-service-token", forwarded.Headers.Authorization?.Parameter);
        Assert.False(forwarded.Headers.Contains("Cookie"));
        Assert.Equal("192.0.2.10", SingleHeader(forwarded, "X-Forwarded-For"));
        Assert.Equal("portal.placecontext.test", SingleHeader(forwarded, "X-Forwarded-Host"));
        Assert.Equal("https", SingleHeader(forwarded, "X-Forwarded-Proto"));
        Assert.Equal("request-42", SingleHeader(forwarded, "X-Request-Id"));
        Assert.Equal("{\"jobId\":1}", forwardedBody);

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.Equal("jobs", context.Response.Headers["X-Upstream"]);
        Assert.False(context.Response.Headers.ContainsKey("Connection"));
        Assert.Equal("{\"id\":42}", await ReadResponseBody(context));
    }

    [Fact]
    public async Task Returns_503_for_an_owned_route_without_a_configured_destination()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(
            new RecordingHandler(_ => throw new InvalidOperationException("No request expected.")),
            new Dictionary<string, string>(),
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });
        var context = CreateContext("GET", "/api/search", "?term=map");

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Contains("Search microservice is not configured", await ReadResponseBody(context));
    }

    [Fact]
    public async Task Returns_502_when_the_configured_microservice_is_unavailable()
    {
        var middleware = CreateMiddleware(
            new RecordingHandler(_ => throw new HttpRequestException("connection refused")),
            new Dictionary<string, string> { ["Vault"] = "http://vault.internal" });
        var context = CreateContext("GET", "/api/vault/projects/one/secrets");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.Contains("Vault microservice is unavailable", await ReadResponseBody(context));
    }

    [Fact]
    public async Task Passes_non_service_routes_to_the_next_middleware()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(
            new RecordingHandler(_ => throw new InvalidOperationException("No request expected.")),
            new Dictionary<string, string>(),
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });
        var context = CreateContext("GET", "/health");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    private static MicroserviceProxyMiddleware CreateMiddleware(
        HttpMessageHandler handler,
        Dictionary<string, string> destinations,
        RequestDelegate? next = null) =>
        new(
            next ?? (_ => Task.CompletedTask),
            new StubHttpClientFactory(handler),
            Options.Create(new MicroserviceProxyOptions { Destinations = destinations }),
            NullLogger<MicroserviceProxyMiddleware>.Instance);

    private static DefaultHttpContext CreateContext(
        string method,
        string path,
        string? query = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("portal.placecontext.test");
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query ?? string.Empty);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadResponseBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    private static string SingleHeader(HttpRequestMessage request, string name) =>
        Assert.Single(request.Headers.GetValues(name));

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
