namespace PlaceContext.Host.Controllers.Api;

public sealed record RunJobRequest(
    string? InputPayload = null,
    Guid? RunId = null);
