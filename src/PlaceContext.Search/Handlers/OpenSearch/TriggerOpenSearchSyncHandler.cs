using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class TriggerOpenSearchSyncHandler
    : ICommandHandler<TriggerOpenSearchSyncCommand, OpenSearchSyncView>
{
    private readonly IOpenSearchSyncGateway _gateway;
    public TriggerOpenSearchSyncHandler(IOpenSearchSyncGateway gateway) => _gateway = gateway;

    public Task<OpenSearchSyncView> HandleAsync(
        TriggerOpenSearchSyncCommand command, CancellationToken ct = default)
        => _gateway.TriggerAsync(ct);
}
