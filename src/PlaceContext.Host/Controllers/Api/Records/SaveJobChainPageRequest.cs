namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record SaveJobChainPageRequest(
    string Name,
    string? Description,
    IReadOnlyList<SaveJobChainStageRequest> Stages);
