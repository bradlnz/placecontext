using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlaceContext.Host.Controllers;
using PlaceContext.ServiceDefaults;
using PlaceContext.Settings;
using HostSettingsController = PlaceContext.Host.Controllers.Api.SettingsController;

namespace PlaceContext.Settings.Tests;

public sealed class SettingsCompositionTests
{
    [Fact]
    public void Settings_runtime_can_construct_all_migrated_controllers()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlaceContext:ServiceAuth:SigningKey"] =
                    "0123456789012345678901234567890123456789",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSettingsModule();
        services.AddSettingsInfrastructure(configuration);
        services.AddPlaceContextServiceRuntime(
            configuration,
            typeof(PlaceContext.Settings.Controllers.SettingsServiceController).Assembly);

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType?.Namespace?.StartsWith(
                "PlaceContext.",
                StringComparison.Ordinal) == true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(ActivatorUtilities.CreateInstance<HostSettingsController>(scope.ServiceProvider));
        Assert.NotNull(ActivatorUtilities.CreateInstance<ConnectionsSettingsController>(scope.ServiceProvider));
        Assert.NotNull(ActivatorUtilities.CreateInstance<BackupSettingsController>(scope.ServiceProvider));
    }
}
