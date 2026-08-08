using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Features;

/// <summary>Translates chain-run aggregates to their pipeline read model.</summary>
internal static class ChainRunMapper
{
    public static ChainRunView ToView(ChainRun run) => new(
        Id: run.Id,
        ChainId: run.ChainId,
        ChainName: run.ChainName,
        Status: run.Status.ToString(),
        Steps: run.Steps
            .Select(s => new ChainStepRunView(
                s.Index, s.StageIndex, s.BranchIndex, s.JobId, s.JobName, s.RunId,
                s.Status.ToString(), s.StartedAt, s.FinishedAt,
                s.ActionType, s.Provider, s.ExternalId, s.Error))
            .ToList(),
        FinalOutput: run.FinalOutput,
        StartedAt: run.StartedAt,
        FinishedAt: run.FinishedAt);
}
