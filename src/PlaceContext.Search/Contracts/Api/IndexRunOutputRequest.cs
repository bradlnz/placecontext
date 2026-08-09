namespace PlaceContext.Search.Contracts.Api;

public sealed record IndexRunOutputRequest(
    Guid RunId,
    Guid JobId,
    Guid ProjectId,
    string Text);
