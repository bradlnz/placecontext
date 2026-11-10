using Microsoft.Extensions.Configuration;
using PlaceContext.Host.Tenancy;

namespace PlaceContext.Host.Tests;

public sealed class ManagedSignupRouteTests
{
    private static IConfiguration Settings(string origin) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlaceContext:PublicBaseUrl"] = origin
        }).Build();

    [Fact]
    public void Workspace_destinations_use_configured_origin_not_caller_host()
    {
        var origin = Settings("https://placecontext.io");
        Assert.True(ManagedSignupUrls.IsApexHost("placecontext.io", origin));
        Assert.False(ManagedSignupUrls.IsApexHost("acme.placecontext.io", origin));
        Assert.False(ManagedSignupUrls.IsApexHost("placecontext.io.evil.test", origin));
        Assert.Equal("https://acme-ops.placecontext.io/login",
            ManagedSignupUrls.TenantUri(origin, "acme-ops", "/login").AbsoluteUri);
        Assert.Throws<ArgumentException>(() => ManagedSignupUrls.TenantUri(origin, "api", "/login"));
    }

    [Theory]
    [InlineData("https://evil.attacker.test")]
    [InlineData("https://placecontext.io.attacker.test")]
    [InlineData("http://placecontext.io")]
    [InlineData("https://placecontext.io/sneaky")]
    [InlineData("https://user:pass@placecontext.io")]
    public void Refuses_untrusted_signup_origins(string origin)
        => Assert.Null(ManagedSignupUrls.PublicOrigin(Settings(origin)));

    [Fact]
    public void Self_hosted_localhost_can_use_workspace_subdomains()
    {
        var local = Settings("http://localhost:7700");
        Assert.Equal("http://widget-team.localhost:7700/auth/signup/complete",
            ManagedSignupUrls.TenantUri(local, "widget-team", "/auth/signup/complete").AbsoluteUri);
    }

    [Fact]
    public void Only_public_auth_routes_may_skip_apex_tenant_lookup()
    {
        Assert.True(ManagedSignupUrls.IsPublicApexPath("/signup"));
        Assert.True(ManagedSignupUrls.IsPublicApexPath("/auth/signup/verify"));
        Assert.True(ManagedSignupUrls.IsPublicApexPath("/login"));
        Assert.True(ManagedSignupUrls.IsPublicApexPath("/healthz"));
        Assert.True(ManagedSignupUrls.IsPublicApexPath("/_blazor/negotiate"));
        Assert.False(ManagedSignupUrls.IsPublicApexPath("/settings/access"));
        Assert.False(ManagedSignupUrls.IsPublicApexPath("/api/v1/projects"));
        Assert.False(ManagedSignupUrls.IsPublicApexPath("/auth/signup/complete"));
        var middleware = Source("src/PlaceContext.Host/Middleware/TenantResolutionMiddleware.cs");
        Assert.Contains("context.Response.Redirect(\"/login\")", middleware);
    }

    [Fact]
    public void Hosted_setup_and_machine_operator_login_are_blocked_in_controller()
    {
        var code = Source("src/PlaceContext.Host/Controllers/AuthController.cs");
        Assert.Contains("if (_config.GetValue<bool>(\"PlaceContext:SaaS:ManagedHosting\"))", code);
        Assert.Contains("return NotFound();", code);
    }

    private static string Source(string path)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PlaceContext.slnx")))
        {
            current = current.Parent;
        }
        if (current is null)
        {
            throw new FileNotFoundException("Repository root not found");
        }
        return File.ReadAllText(Path.Combine(current.FullName, path));
    }
}
