namespace PlaceContext.Crm.Contracts.Api;

public sealed record CustomerPortalJobChainResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    IReadOnlyList<CustomerPortalJobChainStepResponse> Steps);
