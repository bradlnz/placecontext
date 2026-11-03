using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Jobs;

namespace PlaceContext.Application.Features;

public sealed class ListSourceQueuesHandler(IChainQueueService queues) : IQueryHandler<ListSourceQueuesQuery, IReadOnlyList<ChainQueueDefinition>>
{
    public Task<IReadOnlyList<ChainQueueDefinition>> HandleAsync(ListSourceQueuesQuery request, CancellationToken ct = default)
        => queues.ListAsync(request.ProjectId, ct);
}
