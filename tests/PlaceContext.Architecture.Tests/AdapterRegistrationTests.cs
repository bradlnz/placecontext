using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Build;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Organization;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Billing;
using PlaceContext.Infrastructure.Mcp;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.Infrastructure;

namespace PlaceContext.Architecture.Tests;

public sealed class AdapterRegistrationTests
{
    [Theory]
    [InlineData(typeof(IRunBillingWebhook), typeof(PaymentBillingWebhook))]
    [InlineData(typeof(IRunBillingPortal), typeof(PaymentBillingPortal))]
    [InlineData(typeof(ITenantRuntime), typeof(ClusterTenantRuntime))]
    [InlineData(typeof(IModelCredentialsResolver), typeof(ModelCredentialsResolver))]
    [InlineData(typeof(IOutcomeReviewer), typeof(ModelOutcomeReviewer))]
    [InlineData(typeof(IMcpClientService), typeof(McpClientService))]
    [InlineData(typeof(IMcpConnectionProbe), typeof(HttpMcpConnectionProbe))]
    [InlineData(typeof(ITenantBrandingStore), typeof(EfTenantBrandingStore))]
    public void Composition_binds_the_application_contract_to_its_adapter(Type contract, Type adapter)
    {
        var services = new ServiceCollection().AddInfrastructure(new ConfigurationBuilder().Build());
        var registration = Assert.Single(services, service => service.ServiceType == contract);
        Assert.Equal(adapter, registration.ImplementationType);
        Assert.True(contract.IsAssignableFrom(adapter));
        Assert.Equal(typeof(IUnitOfWork).Assembly, contract.Assembly);
    }
}
