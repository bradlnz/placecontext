namespace PlaceContext.Domain.ValueObjects;

public sealed record NetworkSecurityGroup(Guid Id, string Name, IReadOnlyList<NetworkRule> Rules);
