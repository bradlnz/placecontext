namespace PlaceContext.Host.Controllers;

public sealed record RunJobChainRequest(
    Guid ProjectId,
    string? InputPayload = null,
    IReadOnlyDictionary<int, string>? StepPayloadOverrides = null,
    Guid? ClientId = null);
