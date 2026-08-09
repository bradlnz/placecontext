namespace PlaceContext.App.Dashboard;

public sealed record RunChainRequest(string? InputPayload, IReadOnlyDictionary<int, string>? StepPayloadOverrides);
