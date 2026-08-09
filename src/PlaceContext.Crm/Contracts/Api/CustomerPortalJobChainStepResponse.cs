namespace PlaceContext.Crm.Contracts.Api;

public sealed record CustomerPortalJobChainStepResponse(
    int Index,
    Guid JobId,
    string JobName,
    IReadOnlyList<CustomerPortalJobParameterResponse> Parameters,
    string? ConditionExpression);
