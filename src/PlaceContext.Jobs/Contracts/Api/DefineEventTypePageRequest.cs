namespace PlaceContext.Jobs.Contracts.Api;

public sealed record DefineEventTypePageRequest(
    string Name,
    string? Description,
    string? PayloadSchema);
