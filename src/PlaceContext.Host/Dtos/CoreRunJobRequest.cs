namespace PlaceContext.Host.Api;

public sealed record CoreRunJobRequest(
    string? InputPayload = null,
    Guid? RunId = null);
