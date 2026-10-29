using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;
public sealed record JobNetworkAssignment(Guid JobId, Guid NetworkId, Guid GroupId);
