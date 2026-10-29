using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Cluster;

/// <summary>The cluster/nodes inventory for the Cluster page.</summary>
public sealed record GetClusterInfoQuery : IQuery<ClusterInfo>;
