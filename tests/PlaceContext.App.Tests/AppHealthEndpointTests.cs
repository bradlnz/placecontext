using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.App.Tests;

public sealed class AppHealthEndpointTests
{
    [Fact]
    public async Task Legacy_healthz_route_keeps_the_cookieless_json_contract()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHealthChecks();
        await using var app = builder.Build();
        AppHealthEndpoints.Map(app);
        var endpoint = Assert.Single(RouteEndpoints(app), candidate =>
            candidate.RoutePattern.RawText == "/healthz");
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
            Response = { Body = new MemoryStream() },
        };
        context.Request.Headers.Cookie = "placecontext.identity=session";

        await endpoint.RequestDelegate!(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
        Assert.Equal("\"ok\"", await reader.ReadToEndAsync());
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact]
    public void Standard_health_route_remains_mapped()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHealthChecks();
        using var app = builder.Build();

        AppHealthEndpoints.Map(app);

        Assert.Contains(RouteEndpoints(app), candidate => candidate.RoutePattern.RawText == "/health");
    }

    private static IEnumerable<RouteEndpoint> RouteEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();
}
