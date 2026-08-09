using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

public sealed class ListRecentChainRunTelemetryHandler
    : IQueryHandler<ListRecentChainRunTelemetryQuery, IReadOnlyList<ChainRunTelemetry>>
{
    private readonly IJobTelemetryReader _reader;

    public ListRecentChainRunTelemetryHandler(IJobTelemetryReader reader) => _reader = reader;

    public Task<IReadOnlyList<ChainRunTelemetry>> HandleAsync(ListRecentChainRunTelemetryQuery query, CancellationToken ct = default)
        => Task.FromResult(_reader.RecentChainRuns(query.Take));
}
