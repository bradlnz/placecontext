using PlaceContext.Application.Cqrs;
namespace PlaceContext.Application.Cluster;

/// <summary>The cluster/nodes inventory for the Cluster page.</summary>
public sealed record GetClusterInfoQuery : IQuery<ClusterInfo>;
