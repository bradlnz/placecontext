namespace PlaceContext.Jobs.Infrastructure.Integration;

internal sealed record IndexRunOutputRequest(
    Guid RunId,
    Guid JobId,
    Guid ProjectId,
    string Text);
