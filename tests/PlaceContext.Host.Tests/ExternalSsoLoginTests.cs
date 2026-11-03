using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Access;
using PlaceContext.Host.Controllers;

namespace PlaceContext.Host.Tests;

public sealed class ExternalSsoLoginTests
{
    [Theory]
    [InlineData("admin", UserRole.Admin)]
    [InlineData(" ADMIN ", UserRole.Admin)]
    [InlineData("administrator", UserRole.Admin)]
    [InlineData("owner", UserRole.Admin)]
    [InlineData("member", UserRole.Viewer)]
    [InlineData("customer", UserRole.Viewer)]
    [InlineData("superadmin", UserRole.Viewer)]
    [InlineData(null, UserRole.Viewer)]
    public void Only_recognized_provider_admin_roles_grant_admin_access(string? role, UserRole expected)
        => Assert.Equal(expected, SsoSessionsController.ExternalRole(role));

    [Fact]
    public void Complete_https_configuration_enables_external_login()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlaceContext:Sso:Authority"] = "https://auth.lan/realms/homelab",
            ["PlaceContext:Sso:ClientId"] = "placecontext-native",
            ["PlaceContext:Sso:CallbackUrl"] = "https://placecontext.lan/auth/sso/callback",
        }).Build();

        Assert.True(AuthController.ExternalSsoConfigured(configuration));
    }

    [Theory]
    [InlineData("PlaceContext:Sso:Authority")]
    [InlineData("PlaceContext:Sso:ClientId")]
    [InlineData("PlaceContext:Sso:CallbackUrl")]
    public void Incomplete_configuration_keeps_local_login(string missingSetting)
    {
        var values = new Dictionary<string, string?>
        {
            ["PlaceContext:Sso:Authority"] = "https://auth.lan/realms/homelab",
            ["PlaceContext:Sso:ClientId"] = "placecontext-native",
            ["PlaceContext:Sso:CallbackUrl"] = "https://placecontext.lan/auth/sso/callback",
        };
        values.Remove(missingSetting);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.False(AuthController.ExternalSsoConfigured(configuration));
    }
}
