using PlaceContext.Domain.ValueObjects;
namespace PlaceContext.Application.Dtos;
public sealed record SubWorkflowChainActionView(Guid WorkflowId, string WorkflowName)
    : ChainActionView(SubWorkflowChainAction.ActionType, $"Sub-workflow · {WorkflowName}");
