namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a job chain definition.</summary>
public sealed record JobChainView(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    IReadOnlyList<JobChainStepView> Steps,
    DateTimeOffset UpdatedAt);
