namespace PlaceContext.Jobs.Contracts.Api;

public sealed record InternalRunJobRequest(Guid ProjectId, string? InputPayload = null);
