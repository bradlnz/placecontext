namespace PlaceContext.Jobs.Contracts.Api;

public sealed record EventTypePageResponse(
    string Name,
    string? Description,
    bool IsBuiltIn,
    string? PayloadSchema);
