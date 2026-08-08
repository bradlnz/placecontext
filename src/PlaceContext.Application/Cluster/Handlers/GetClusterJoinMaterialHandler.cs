using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Cluster;

public sealed class GetClusterJoinMaterialHandler : IQueryHandler<GetClusterJoinMaterialQuery, ClusterJoinMaterial?>
{
    private readonly IClusterAdminPort _admin;

    public GetClusterJoinMaterialHandler(IClusterAdminPort admin) => _admin = admin;

    public Task<ClusterJoinMaterial?> HandleAsync(GetClusterJoinMaterialQuery query, CancellationToken ct = default)
        => _admin.GetJoinMaterialAsync(ct);
}
