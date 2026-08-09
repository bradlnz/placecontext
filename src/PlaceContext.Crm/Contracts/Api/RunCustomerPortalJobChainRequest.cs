namespace PlaceContext.Crm.Contracts.Api;

public sealed record RunCustomerPortalJobChainRequest(
    Guid ProjectId,
    string? InputPayload = null,
    IReadOnlyDictionary<int, string>? StepPayloadOverrides = null,
    Guid? ClientId = null);
