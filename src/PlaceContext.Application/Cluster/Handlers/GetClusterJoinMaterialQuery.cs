using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Cluster;

/// <summary>
/// Mint (or load) a join code so a new machine can join the fleet over Tailscale, targeting the
/// designated master. Null when the cluster join secret has not been seeded on this install.
/// </summary>
public sealed record GetClusterJoinMaterialQuery : IQuery<ClusterJoinMaterial?>;

public sealed class GetClusterJoinMaterialHandler : IQueryHandler<GetClusterJoinMaterialQuery, ClusterJoinMaterial?>
{
    private readonly IClusterAdminPort _admin;

    public GetClusterJoinMaterialHandler(IClusterAdminPort admin) => _admin = admin;

    public Task<ClusterJoinMaterial?> HandleAsync(GetClusterJoinMaterialQuery query, CancellationToken ct = default)
        => _admin.GetJoinMaterialAsync(ct);
}
