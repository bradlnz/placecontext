namespace PlaceContext.Data.Contracts.Api;

public sealed record EntityRunJobRequest(string? InputPayload = null, Guid? RunId = null);
