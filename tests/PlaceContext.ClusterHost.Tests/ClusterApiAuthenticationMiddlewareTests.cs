using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PlaceContext.ClusterHost;

namespace PlaceContext.ClusterHost.Tests;

public sealed class ClusterApiAuthenticationMiddlewareTests
{
    [Fact]
    public async Task Compute_routes_fail_closed_without_configuration()
    {
        var (context, called) = await InvokeAsync("/api/cluster/embeddings", configured: "", supplied: null);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.False(called());
    }

    [Fact]
    public async Task Compute_routes_reject_an_invalid_token()
    {
        var (context, called) = await InvokeAsync("/api/cluster/chat", configured: "correct-token-value-1234567890", supplied: "wrong");

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(called());
    }

    [Fact]
    public async Task Compute_routes_accept_the_shared_token()
    {
        var token = "correct-token-value-1234567890";
        var (context, called) = await InvokeAsync("/api/cluster/chat/stream", token, token);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.True(called());
    }

    [Fact]
    public async Task Health_remains_available_to_cluster_probes()
    {
        var (context, called) = await InvokeAsync("/api/cluster/health", configured: "", supplied: null);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.True(called());
    }

    private static async Task<(DefaultHttpContext Context, Func<bool> Called)> InvokeAsync(
        string path,
        string configured,
        string? supplied)
    {
        var called = false;
        var middleware = new ClusterApiAuthenticationMiddleware(context =>
        {
            called = true;
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (supplied is not null)
            context.Request.Headers[ClusterApiAuthenticationMiddleware.HeaderName] = supplied;

        await middleware.InvokeAsync(
            context,
            Options.Create(new ClusterProxyOptions { ApiToken = configured }));
        return (context, () => called);
    }
}
