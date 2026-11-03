using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Search;
namespace PlaceContext.Application.Features;

public sealed class TriggerSearchSyncHandler
    : ICommandHandler<TriggerSearchSyncCommand, SearchSyncView>
{
    private readonly ISearchSyncGateway _gateway;
    public TriggerSearchSyncHandler(ISearchSyncGateway gateway) => _gateway = gateway;

    public Task<SearchSyncView> HandleAsync(
        TriggerSearchSyncCommand command, CancellationToken ct = default)
        => _gateway.TriggerAsync(ct);
}
