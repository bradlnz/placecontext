using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Runtime;

namespace PlaceContext.App.Tests;

public sealed class ServiceRequestContextMiddlewareTests
{
    [Fact]
    public async Task Api_key_request_resolves_tenant_from_original_forwarded_host()
    {
        var tenant = new TenantContext(Guid.NewGuid(), "acme", "Australia/Brisbane");
        var resolver = new RecordingTenantResolver(tenant);
        var currentTenant = new ServiceCurrentTenant();
        var currentUser = new ServiceCurrentUser();
        var observedResolvedTenant = false;
        var middleware = new ServiceRequestContextMiddleware(_ =>
        {
            observedResolvedTenant = currentTenant.IsResolved
                && currentTenant.TenantId == tenant.Id;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-Host"] = "acme.placecontext.ai";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", ServiceApiKeyAuthenticationDefaults.Subject),
                new Claim("role", "Owner"),
            ],
            ServiceApiKeyAuthenticationDefaults.Scheme));

        await middleware.InvokeAsync(context, currentTenant, currentUser, resolver);

        Assert.True(observedResolvedTenant);
        Assert.Equal("acme.placecontext.ai", resolver.Host);
        Assert.False(currentTenant.IsResolved);
        Assert.False(currentUser.IsAuthenticated);
    }

    [Fact]
    public async Task Signed_tenant_claim_takes_precedence_over_host_resolution()
    {
        var tenantId = Guid.NewGuid();
        var resolver = new RecordingTenantResolver(
            new TenantContext(Guid.NewGuid(), "wrong", "UTC"));
        var currentTenant = new ServiceCurrentTenant();
        var currentUser = new ServiceCurrentUser();
        Guid observedTenantId = Guid.Empty;
        var middleware = new ServiceRequestContextMiddleware(_ =>
        {
            observedTenantId = currentTenant.TenantId;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("tenant", tenantId.ToString()),
                new Claim("tenant_slug", "signed"),
            ],
            "ServiceBearer"));

        await middleware.InvokeAsync(context, currentTenant, currentUser, resolver);

        Assert.Equal(tenantId, observedTenantId);
        Assert.Null(resolver.Host);
    }

    [Fact]
    public async Task Api_key_request_fails_closed_when_no_tenant_resolver_is_configured()
    {
        var nextCalled = false;
        var middleware = new ServiceRequestContextMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", ServiceApiKeyAuthenticationDefaults.Subject)],
            ServiceApiKeyAuthenticationDefaults.Scheme));

        await middleware.InvokeAsync(
            context,
            new ServiceCurrentTenant(),
            new ServiceCurrentUser(),
            new RecordingTenantResolver(null));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    private sealed class RecordingTenantResolver(TenantContext? result) : IRequestTenantResolver
    {
        public string? Host { get; private set; }

        public Task<TenantContext?> ResolveAsync(string host, CancellationToken ct = default)
        {
            Host = host;
            return Task.FromResult<TenantContext?>(result);
        }
    }
}
