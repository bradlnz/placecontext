using PlaceContext.Host.Tenancy;

namespace PlaceContext.Host.Tests;

public class PublicUrlAndTenantSlugTests
{
    [Theory]
    [InlineData("feasibility.example.com", "https://feasibility.example.com", true)]
    [InlineData("FEASIBILITY.EXAMPLE.COM", "https://feasibility.example.com", true)]
    [InlineData("tenant.example.com", "https://example.com", true)]
    [InlineData("10.42.0.58", "https://feasibility.example.com", false)]
    [InlineData("placecontext.placecontext.svc.cluster.local", "https://feasibility.example.com", false)]
    [InlineData("localhost", "https://feasibility.example.com", false)]
    [InlineData("feasibility.example.com", "http://feasibility.example.com", false)]
    [InlineData("feasibility.example.com", null, false)]
    [InlineData("example.com.attacker.test", "https://example.com", false)]
    public void Https_redirect_applies_to_public_hosts_not_internal_service_calls(string host, string? origin, bool expected)
        => Assert.Equal(expected, PublicUrl.RequiresHttps(host, origin));

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("brad.localhost", true)]
    [InlineData("acme.placecontext.io", true)]
    [InlineData("placecontext.io", true)]
    [InlineData("acme.placecontext.ai", false)]
    [InlineData("evil.attacker", false)]
    [InlineData("foo.evil.com", false)]
    [InlineData("evil.com", false)]
    public void Trusted_hosts(string host, bool trusted) =>
        Assert.Equal(trusted, PublicUrl.IsTrustedHost(host));

    [Theory]
    [InlineData("localhost", "default")]
    [InlineData("127.0.0.1", "default")]
    [InlineData("brad.localhost", "brad")]
    [InlineData("acme.placecontext.io", "acme")]
    [InlineData("acme.placecontext.ai", "default")]
    [InlineData("evil.attacker", "default")] // was: would not match 3-part rule; still default
    [InlineData("foo.evil.com", "default")] // was: slug "foo" — Host-header tenant minting
    [InlineData("sub.foo.evil.com", "default")]
    public void ResolveSlug_only_known_bases(string host, string expected) =>
        Assert.Equal(expected, TenantResolutionMiddleware.ResolveSlug(host));
}
