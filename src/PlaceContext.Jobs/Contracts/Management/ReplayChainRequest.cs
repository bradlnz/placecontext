namespace PlaceContext.Jobs.Contracts.Management;

public sealed record ReplayChainRequest(
    Guid OriginalRunId,
    int? FromStepIndex = null,
    string? InputPayload = null,
    IReadOnlyDictionary<int, string>? StepPayloadOverrides = null);
