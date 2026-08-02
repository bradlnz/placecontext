using PlaceContext.Application.Features;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

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
