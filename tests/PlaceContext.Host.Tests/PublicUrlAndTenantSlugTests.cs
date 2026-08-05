using PlaceContext.Host.Tenancy;

namespace PlaceContext.Host.Tests;

public class PublicUrlAndTenantSlugTests
{
    [Theory]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("brad.localhost", true)]
    [InlineData("acme.placecontext.ai", true)]
    [InlineData("placecontext.ai", true)]
    [InlineData("evil.attacker", false)]
    [InlineData("foo.evil.com", false)]
    [InlineData("evil.com", false)]
    public void Trusted_hosts(string host, bool trusted) =>
        Assert.Equal(trusted, PublicUrl.IsTrustedHost(host));

    [Theory]
    [InlineData("localhost", "default")]
    [InlineData("127.0.0.1", "default")]
    [InlineData("brad.localhost", "brad")]
    [InlineData("acme.placecontext.ai", "acme")]
    [InlineData("evil.attacker", "default")] // was: would not match 3-part rule; still default
    [InlineData("foo.evil.com", "default")] // was: slug "foo" — Host-header tenant minting
    [InlineData("sub.foo.evil.com", "default")]
    public void ResolveSlug_only_known_bases(string host, string expected) =>
        Assert.Equal(expected, TenantResolutionMiddleware.ResolveSlug(host));
}
