using Microsoft.Extensions.Configuration;
using PlaceContext.Host.Controllers;

namespace PlaceContext.Host.Tests;

public sealed class ExternalSsoLoginTests
{
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
