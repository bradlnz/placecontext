namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record SaveJobTestBlockRequest(
    Guid JobId,
    string Name,
    string? InputPayload,
    string AssertionType,
    string? ExpectedValue,
    bool Enabled);
