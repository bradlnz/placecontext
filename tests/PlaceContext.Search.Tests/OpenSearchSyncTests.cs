using PlaceContext.Application.Features;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Cqrs;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Search;

namespace PlaceContext.Application.Tests;

public sealed class OpenSearchSyncTests
{
    [Fact]
    public async Task Manual_sync_delegates_to_the_collector_trigger()
    {
        var expected = new OpenSearchSyncView(true, "queued", "Collector sync queued.");
        var gateway = new StubSyncGateway(expected);
        var handler = new TriggerOpenSearchSyncHandler(gateway);

        var result = await handler.HandleAsync(new TriggerOpenSearchSyncCommand(Guid.NewGuid()));

        Assert.Equal(expected, result);
        Assert.Equal(1, gateway.TriggerCount);
    }

    [Fact]
    public void Manual_sync_handler_is_registered_with_search()
    {
        var services = new ServiceCollection();

        services.AddSearchModule();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICommandHandler<
                TriggerOpenSearchSyncCommand,
                OpenSearchSyncView>)
            && descriptor.ImplementationType == typeof(TriggerOpenSearchSyncHandler));
    }

    private sealed class StubSyncGateway(OpenSearchSyncView result) : IOpenSearchSyncGateway
    {
        public int TriggerCount { get; private set; }

        public Task<OpenSearchSyncView> TriggerAsync(CancellationToken ct = default)
        {
            TriggerCount++;
            return Task.FromResult(result);
        }
    }
}
