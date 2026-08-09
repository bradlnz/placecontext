using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Projects;
using PlaceContext.Projects.Infrastructure.Tenancy;

namespace PlaceContext.Projects.Tests;

public sealed class HttpIdentityTenantResolverTests
{
    [Fact]
    public async Task Resolves_forwarded_host_through_authenticated_Identity_endpoint()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"id":"b58a2795-49e7-4ef4-9a79-e42e36fd918a","slug":"acme","timeZoneId":"Australia/Sydney"}""");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlaceContext:Projects:Identity:BaseAddress"] = "https://identity.internal/base",
                ["PlaceContext:Projects:Identity:ServiceToken"] = "service-token",
            })
            .Build();
        var resolver = new HttpIdentityTenantResolver(
            new StubHttpClientFactory(new HttpClient(handler)),
            configuration);

        var tenant = await resolver.ResolveAsync("acme.example.test");

        Assert.NotNull(tenant);
        Assert.Equal("acme", tenant.Slug);
        Assert.Equal("Australia/Sydney", tenant.TimeZoneId);
        Assert.Equal(
            "https://identity.internal/base/api/identity/internal/tenants/resolve?host=acme.example.test",
            handler.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("service-token", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task Missing_Identity_tenant_returns_null()
    {
        var handler = new CapturingHandler(HttpStatusCode.NotFound, string.Empty);
        var resolver = new HttpIdentityTenantResolver(
            new StubHttpClientFactory(new HttpClient(handler)),
            Configuration());

        var tenant = await resolver.ResolveAsync("missing.example.test");

        Assert.Null(tenant);
    }

    [Fact]
    public void Projects_composition_registers_the_Identity_tenant_resolver()
    {
        var services = new ServiceCollection();
        var configuration = Configuration();
        services.AddSingleton(configuration);

        services.AddProjectsInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<HttpIdentityTenantResolver>(
            provider.GetRequiredService<IRequestTenantResolver>());
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlaceContext:Projects:Identity:BaseAddress"] = "https://identity.internal",
            ["PlaceContext:Projects:Identity:ServiceToken"] = "service-token",
        })
        .Build();

    private sealed class CapturingHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
