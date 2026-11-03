using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Search;

namespace PlaceContext.Application.Tests;

public sealed class SearchSyncTests
{
    [Fact]
    public async Task Manual_sync_delegates_to_the_collector_trigger()
    {
        var expected = new SearchSyncView(true, "queued", "Collector sync queued.");
        var gateway = new StubSyncGateway(expected);
        var handler = new TriggerSearchSyncHandler(gateway);

        var result = await handler.HandleAsync(new TriggerSearchSyncCommand(Guid.NewGuid()));

        Assert.Equal(expected, result);
        Assert.Equal(1, gateway.TriggerCount);
    }

    [Fact]
    public void Manual_sync_handler_is_registered_with_the_application()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICommandHandler<
                TriggerSearchSyncCommand,
                SearchSyncView>)
            && descriptor.ImplementationType == typeof(TriggerSearchSyncHandler));
    }

    private sealed class StubSyncGateway(SearchSyncView result) : ISearchSyncGateway
    {
        public int TriggerCount
        {
            get; private set;
        }

        public Task<SearchSyncView> TriggerAsync(CancellationToken ct = default)
        {
            TriggerCount++;
            return Task.FromResult(result);
        }
    }
}
