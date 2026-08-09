namespace PlaceContext.Jobs.Contracts.Api;

public sealed record SaveJobChainPageRequest(
    string Name,
    string? Description,
    IReadOnlyList<SaveJobChainStageRequest> Stages);
