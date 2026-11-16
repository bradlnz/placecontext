using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests.Billing;

public sealed class CloudConfigurationTests
{
    [Theory]
    [InlineData("BasicRunsPerPeriod")]
    [InlineData("StandardRunsPerPeriod")]
    [InlineData("BasicAiRequestsPerPeriod")]
    [InlineData("StandardAiRequestsPerPeriod")]
    public void Managed_limits_must_be_positive(string setting)
    {
        using var services = Provider(new() { ["PlaceContext:SaaS:ManagedHosting"] = "true", [$"PlaceContext:SaaS:{setting}"] = "0" });
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<RunBillingDeploymentOptions>>().Value);
    }

    [Fact]
    public void Managed_agents_cannot_bypass_usage_accounting_with_a_host_cli()
    {
        using var services = Provider(new() { ["PlaceContext:SaaS:ManagedHosting"] = "true", ["PlaceContext:Qemu:Agent:Provider"] = "codex" });
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<VirtualMachineEnvironmentOptions>>().Value);
    }

    private static ServiceProvider Provider(Dictionary<string, string?> values) => new ServiceCollection()
        .AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();
}
