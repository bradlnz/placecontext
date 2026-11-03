using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Networking;

public sealed record WorkerNetwork(Guid Id, Guid ProjectId, string Name, string Cidr,
    IReadOnlyList<NetworkSecurityGroup> Groups, long Version = 0);
