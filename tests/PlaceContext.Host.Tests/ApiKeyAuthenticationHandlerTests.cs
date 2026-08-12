using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Host.Auth;
using Xunit;

namespace PlaceContext.Host.Tests;

/// <summary>
/// Exercises <see cref="ApiKeyAuthenticationHandler"/> directly through ASP.NET Core's real
/// authentication pipeline (a minimal DI container + <see cref="DefaultHttpContext"/>) rather than a full
/// TestServer — the handler has no DB dependency, and standing up the whole Host (tenant resolution,
/// Postgres-backed data protection, etc.) would make this anything but lightweight. Covers the contract
/// the management API's auth model rests on: deny-by-default when unconfigured, 401 on a wrong/missing
/// key, both header forms accepted, and a successful match yielding an Owner-role principal.
/// </summary>
public class ApiKeyAuthenticationHandlerTests
{
    private const string ConfiguredKey = "s3cr3t-terraform-key";

    [Fact]
    public async Task Fails_when_no_key_is_configured()
    {
        var result = await AuthenticateAsync(
            configuredKey: null,
            req => req.Headers.Authorization = $"Bearer {ConfiguredKey}"
        );

        Assert.False(result.Succeeded);
        Assert.Contains("not configured", result.Failure!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fails_when_no_key_is_presented()
    {
        var result = await AuthenticateAsync(ConfiguredKey, req => { });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Fails_when_the_presented_key_is_wrong()
    {
        var result = await AuthenticateAsync(
            ConfiguredKey,
            req => req.Headers.Authorization = "Bearer wrong-key"
        );

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Succeeds_with_a_correct_bearer_key_and_grants_an_owner_principal()
    {
        var result = await AuthenticateAsync(
            ConfiguredKey,
            req => req.Headers.Authorization = $"Bearer {ConfiguredKey}"
        );

        Assert.True(result.Succeeded);
        Assert.Equal(
            ApiKeyAuthenticationHandler.PrincipalId.ToString(),
            result.Principal!.FindFirst("sub")!.Value
        );
        Assert.Equal("Owner", result.Principal.FindFirst("role")!.Value);
    }

    [Fact]
    public async Task Succeeds_with_a_correct_X_Api_Key_header()
    {
        var result = await AuthenticateAsync(
            ConfiguredKey,
            req => req.Headers["X-Api-Key"] = ConfiguredKey
        );

        Assert.True(result.Succeeded);
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(
        string? configuredKey,
        Action<HttpRequest> configureRequest
    )
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["PlaceContext:Api:Key"] = configuredKey }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services
            .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName,
                _ => { }
            );
        await using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        configureRequest(context.Request);

        return await context.AuthenticateAsync(ApiKeyAuthenticationHandler.SchemeName);
    }
}
