using PlaceContext.Domain.ValueObjects;
namespace PlaceContext.Application.Dtos;
public sealed record ParallelWorkflowsChainActionView(IReadOnlyList<ConditionalWorkflowBranch> Branches, string OutputField = "parallel_results")
    : ChainActionView(ParallelWorkflowsChainAction.ActionType, "Parallel if gates");
