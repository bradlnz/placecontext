namespace PlaceContext.Crm.Integration;

public sealed record CrmJobChainRun(
    Guid Id,
    Guid ChainId,
    string ChainName,
    string Status,
    IReadOnlyList<CrmJobChainStepRun> Steps,
    string? FinalOutput,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt)
{
    public IReadOnlyList<IReadOnlyList<CrmJobChainStepRun>> StepsByStage => Steps
        .GroupBy(step => step.StageIndex)
        .OrderBy(group => group.Key)
        .Select(group => (IReadOnlyList<CrmJobChainStepRun>)group
            .OrderBy(step => step.BranchIndex)
            .ToList())
        .ToList();
}
