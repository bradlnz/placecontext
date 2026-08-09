namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobChainRunResponse(
    Guid Id,
    Guid ChainId,
    string ChainName,
    string Status,
    IReadOnlyList<JobChainRunStepResponse> Steps,
    string? FinalOutput,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string StartedAtDisplay,
    string? DurationDisplay);
