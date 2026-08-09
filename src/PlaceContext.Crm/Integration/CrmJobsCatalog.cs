namespace PlaceContext.Crm.Integration;

public sealed record CrmJobsCatalog(IReadOnlyList<CrmJobChainSummary> Chains);
