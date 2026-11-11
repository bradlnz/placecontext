using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Build;
using PlaceContext.Host.Controllers;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowDemoControllerTests
{
    [Fact]
    public async Task Public_demo_encodes_untrusted_content_and_is_sandboxed_uncached_and_read_only()
    {
        var service = new Shares
        {
            Demo = new("<script>title</script>", "Succeeded", "</pre><script>alert(1)</script>", DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(1), [new("</script><img src=https://example.test>", 1000, "AAAA")])
        };
        var controller = new WorkflowDemoController(service) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var result = Assert.IsType<ContentResult>(await controller.Get("token"));
        Assert.Contains("&lt;script&gt;title", result.Content);
        Assert.DoesNotContain("<script>alert(1)", result.Content);
        Assert.DoesNotContain("<img src=https://example.test>", result.Content);
        Assert.Contains("textContent", result.Content);
        Assert.Contains("sandbox allow-scripts; default-src 'none'", controller.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.Contains("script-src 'sha256-", controller.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.DoesNotContain("allow-same-origin", controller.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        Assert.Equal("no-referrer", controller.Response.Headers["Referrer-Policy"]);
        Assert.True(typeof(WorkflowDemoController).IsDefined(typeof(AllowAnonymousAttribute), true));
        Assert.Equal(0, service.Writes);
        service.Demo = null;
        Assert.IsType<NotFoundResult>(await controller.Get("expired"));
    }

    private sealed class Shares : IWorkflowDemoShares
    {
        public WorkflowDemo? Demo;
        public int Writes;
        public Task<WorkflowDemo?> ResolveAsync(string token, CancellationToken ct = default) => Task.FromResult(Demo);
        public Task<WorkflowDemoLink> CreateAsync(Guid projectId, Guid runId, string outcome, int? replayStep, int lifetimeDays, CancellationToken ct = default)
        {
            Writes++;
            throw new NotSupportedException();
        }
        public Task<DateTimeOffset?> GetExpiryAsync(Guid projectId, Guid runId, CancellationToken ct = default) => Task.FromResult<DateTimeOffset?>(null);
        public Task RevokeAsync(Guid projectId, Guid runId, CancellationToken ct = default)
        {
            Writes++;
            throw new NotSupportedException();
        }
    }
}
