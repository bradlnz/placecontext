using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.ServiceDefaults;

namespace PlaceContext.App.Tests;

public sealed class ServiceApiKeyAuthenticationHandlerTests
{
    [Theory]
    [InlineData("X-Api-Key")]
    [InlineData("Authorization")]
    public async Task Configured_workspace_key_receives_owner_permissions(string header)
    {
        const string key = "test-workspace-key-with-enough-entropy";
        var context = new DefaultHttpContext();
        context.Request.Headers[header] = header == "Authorization" ? $"Bearer {key}" : key;

        var result = await AuthenticateAsync(context, key);

        Assert.True(result.Succeeded);
        Assert.Equal(
            ServiceApiKeyAuthenticationDefaults.Subject,
            result.Principal?.FindFirst("sub")?.Value);
        Assert.Equal(
            Permission.All.Order(),
            result.Principal?.FindAll(ServiceAuthenticationDefaults.PermissionClaim)
                .Select(claim => claim.Value)
                .Order());
    }

    [Fact]
    public async Task Incorrect_workspace_key_is_rejected()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "incorrect";

        var result = await AuthenticateAsync(context, "configured-key");

        Assert.False(result.Succeeded);
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(
        HttpContext context,
        string configuredKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ServiceApiKeyAuthenticationDefaults.ConfigurationKey] = configuredKey,
            })
            .Build();
        var handler = new ServiceApiKeyAuthenticationHandler(
            new StaticAuthenticationOptionsMonitor(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            configuration);
        await handler.InitializeAsync(
            new AuthenticationScheme(
                ServiceApiKeyAuthenticationDefaults.Scheme,
                null,
                typeof(ServiceApiKeyAuthenticationHandler)),
            context);
        return await handler.AuthenticateAsync();
    }

    private sealed class StaticAuthenticationOptionsMonitor
        : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        private static readonly AuthenticationSchemeOptions Value = new();

        public AuthenticationSchemeOptions CurrentValue => Value;

        public AuthenticationSchemeOptions Get(string? name) => Value;

        public IDisposable? OnChange(
            Action<AuthenticationSchemeOptions, string?> listener) => null;
    }
}
