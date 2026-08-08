using PlaceContext.App.Proxy;

namespace PlaceContext.App.Tests;

public sealed class AppAssemblyDependencyTests
{
    private static readonly HashSet<string> ForbiddenAssemblies = new(StringComparer.Ordinal)
    {
        "PlaceContext.Host",
        "PlaceContext.Infrastructure",
        "PlaceContext.AgentChat",
        "PlaceContext.AgentChat.Infrastructure",
        "PlaceContext.Agents",
        "PlaceContext.Agents.Infrastructure",
        "PlaceContext.Artifacts",
        "PlaceContext.Artifacts.Infrastructure",
        "PlaceContext.Crm",
        "PlaceContext.Crm.Infrastructure",
        "PlaceContext.Data",
        "PlaceContext.Data.Infrastructure",
        "PlaceContext.Jobs",
        "PlaceContext.Jobs.Infrastructure",
        "PlaceContext.Search",
        "PlaceContext.Search.Infrastructure",
        "PlaceContext.Vault",
        "PlaceContext.Vault.Infrastructure",
    };

    [Fact]
    public void Gateway_does_not_reference_the_legacy_host_or_service_implementations()
    {
        var references = typeof(MicroserviceProxyMiddleware).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToList();

        Assert.DoesNotContain(references, ForbiddenAssemblies.Contains);
    }
}
