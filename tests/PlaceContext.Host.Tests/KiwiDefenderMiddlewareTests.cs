using System.Security.Claims;
using System.Text.Json;
using KiwiDefender;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using PlaceContext.Host.Security;
using PlaceContext.Host.Tenancy;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Tests;

public class KiwiDefenderMiddlewareTests
{
    [Theory]
    [InlineData("allow", 200, true)]
    [InlineData("deny", 403, false)]
    [InlineData("observe", 200, true)]
    [InlineData("tenant", 403, false)]
    [InlineData("expired", 401, false)]
    [InlineData("anonymous-user", 401, false)]
    [InlineData("missing-subject", 401, false)]
    [InlineData("unavailable", 503, false)]
    [InlineData("expired-policy", 503, false)]
    [InlineData("rate", 429, false)]
    [InlineData("forged-scope", 403, false)]
    [InlineData("public", 200, true)]
    [InlineData("unprotected", 200, true)]
    [InlineData("mcp", 200, true)]
    public async Task Enforces_policy_before_the_endpoint(string scenario, int status, bool allowed)
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var defender = new Defender("placecontext", () => now);
        if (scenario is not ("unavailable" or "public" or "unprotected"))
            defender.Activate(JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1, application = "placecontext", generation = 1,
                not_before = now - 1, expires_at = now + 120,
                mode = scenario == "observe" ? "observe" : "enforce",
                rate = new { limit = 1, window_seconds = 60, max_subjects = 10 },
                rules = new[] { new { id = "search", effect = scenario is "deny" or "observe" ? "deny" : "allow",
                    operation = "http.request", action = "GET", resource = scenario == "mcp" ? "mcp" : "Search.Search",
                    scope = scenario is "deny" or "observe" ? "" : scenario == "forged-scope" ? "admin" : "authenticated" } }
            }));
        var claims = new List<Claim> { new("tenant", scenario == "tenant" ? Guid.NewGuid().ToString() : tenantId.ToString()),
            new("exp", (scenario == "expired" ? now - 1 : now + 60).ToString()) };
        if (scenario != "missing-subject") claims.Add(new Claim("sub", "user-1"));
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, scenario == "anonymous-user" ? null : "test"));
        context.Request.Method = "GET";
        context.Request.Path = scenario == "mcp" ? "/mcp" : "/api/v1/search";
        context.Request.Headers["X-KiwiDefender-Scopes"] = "admin";
        context.Response.Body = new MemoryStream();
        var metadata = new List<object> { new AuthorizeAttribute() };
        if (scenario != "mcp") metadata.Add(new ControllerActionDescriptor { ControllerName = "Search", ActionName = "Search" });
        if (scenario == "public") metadata.Add(new AllowAnonymousAttribute());
        if (scenario == "unprotected") metadata.RemoveAll(item => item is IAuthorizeData);
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));
        var calls = 0;
        var middleware = new KiwiDefenderMiddleware(_ => { calls++; return Task.CompletedTask; }, defender,
            NullLogger<KiwiDefenderMiddleware>.Instance);
        var holder = new TenantHolder { Tenant = new TenantInfo(tenantId, "test", "Test", "UTC") };
        if (scenario == "expired-policy") now += 121;
        if (scenario == "rate") { await middleware.Invoke(context, holder); calls = 0; }
        await middleware.Invoke(context, holder);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(allowed ? 1 : 0, calls);
        if (scenario == "rate") Assert.Equal("60", context.Response.Headers.RetryAfter.ToString());
        if (!allowed) Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Empty(defender.DrainEvents());
    }
}
