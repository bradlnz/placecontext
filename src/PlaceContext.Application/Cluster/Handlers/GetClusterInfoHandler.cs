using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Cluster;

public sealed class GetClusterInfoHandler : IQueryHandler<GetClusterInfoQuery, ClusterInfo>
{
    private readonly IClusterInfoProvider _provider;

    public GetClusterInfoHandler(IClusterInfoProvider provider) => _provider = provider;

    public Task<ClusterInfo> HandleAsync(GetClusterInfoQuery query, CancellationToken ct = default)
        => _provider.GetClusterInfoAsync(ct);
}
