namespace PlaceContext.Artifacts.Contracts.Api;

public sealed record StoreCrmObjectRequest(
    Guid ProjectId,
    Guid ClientId,
    Guid ObjectId,
    string ContentBase64,
    string ContentType);
