using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Jobs.Infrastructure.Persistence;

/// <summary>Backward-compatible persisted wire shape for one chain step.</summary>
internal sealed class ChainRunStepJson
{
    public int Index { get; set; }
    public int? StageIndex { get; set; }
    public int? BranchIndex { get; set; }
    public Guid JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public Guid? RunId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ActionType { get; set; }
    public string? Provider { get; set; }
    public string? ExternalId { get; set; }
    public string? Error { get; set; }

    public static ChainRunStepJson From(ChainStepRun step) => new()
    {
        Index = step.Index,
        StageIndex = step.StageIndex,
        BranchIndex = step.BranchIndex,
        JobId = step.JobId,
        JobName = step.JobName,
        RunId = step.RunId,
        Status = step.Status.ToString(),
        StartedAt = step.StartedAt,
        FinishedAt = step.FinishedAt,
        ActionType = step.ActionType,
        Provider = step.Provider,
        ExternalId = step.ExternalId,
        Error = step.Error,
    };

    public ChainStepRun ToDomain() => new(
        Index,
        StageIndex ?? Index,
        BranchIndex ?? 0,
        JobId,
        JobName,
        RunId,
        Enum.Parse<ChainStepStatus>(Status),
        StartedAt,
        FinishedAt,
        ActionType,
        Provider,
        ExternalId,
        Error);
}
