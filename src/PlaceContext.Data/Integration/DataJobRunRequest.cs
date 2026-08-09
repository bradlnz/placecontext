namespace PlaceContext.Data.Integration;

public sealed record DataJobRunRequest(Guid ProjectId, string? InputPayload, Guid? RunId);
