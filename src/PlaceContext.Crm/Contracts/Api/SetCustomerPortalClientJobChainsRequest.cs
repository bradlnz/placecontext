namespace PlaceContext.Crm.Contracts.Api;

public sealed record SetCustomerPortalClientJobChainsRequest(IReadOnlyList<Guid> ChainIds);
