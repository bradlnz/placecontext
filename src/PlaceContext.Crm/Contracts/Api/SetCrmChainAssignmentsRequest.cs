namespace PlaceContext.Crm.Contracts.Api;

public sealed record SetCrmChainAssignmentsRequest(Guid ProjectId, IReadOnlyList<Guid> ChainIds);
