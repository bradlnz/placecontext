using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Cluster;

/// <summary>The cluster/nodes inventory for the Cluster page.</summary>
public sealed record GetClusterInfoQuery : IQuery<ClusterInfo>;

public sealed class GetClusterInfoHandler : IQueryHandler<GetClusterInfoQuery, ClusterInfo>
{
    private readonly IClusterInfoProvider _provider;

    public GetClusterInfoHandler(IClusterInfoProvider provider) => _provider = provider;

    public Task<ClusterInfo> HandleAsync(GetClusterInfoQuery query, CancellationToken ct = default)
        => _provider.GetClusterInfoAsync(ct);
}
