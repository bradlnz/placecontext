using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Networking;

public sealed record JobNetworkAssignment(Guid JobId, Guid NetworkId, Guid GroupId);
