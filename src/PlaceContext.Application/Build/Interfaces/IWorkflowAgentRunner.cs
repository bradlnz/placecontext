using System.Text.Json.Nodes;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Build;

public interface IWorkflowAgentRunner
{
    Task<JsonNode> RunAsync(Guid projectId, Guid workflowId, Guid runId, int stepIndex,
        LoopChainAction action, string? payload, CancellationToken ct, Action<AgentActivity>? report = null);
}
