using PlaceContext.Application.AgentWorkflows;

namespace PlaceContext.Host.Tests;

internal sealed class AgentWorkflowUiStub : IAgentWorkflows
{
    public AgentWorkflow Workflow { get; } = new("services", "Client onboarding", "Professional services", "Prepare a plan", true, ["Collect", "Review"], ["client"]);
    public bool Active;
    public int Runs;
    public List<Guid> RequestIds { get; } = [];
    public bool FailOnce;
    public TaskCompletionSource? Pause;
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<AgentWorkspace> LoadAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(
        new AgentWorkspace(true, true, [Workflow], [new(Workflow.Id, Active, DateTimeOffset.UtcNow)], []));
    public Task SetSubscriptionAsync(Guid projectId, string agentId, bool active, CancellationToken ct = default)
    { Active = active; return Task.CompletedTask; }
    public async Task<AgentRun> RunAsync(Guid projectId, string agentId, Guid requestId, string input, CancellationToken ct = default)
    {
        Runs++; RequestIds.Add(requestId); Started.TrySetResult();
        if (Pause is not null) await Pause.Task.WaitAsync(ct);
        if (FailOnce) { FailOnce = false; throw new InvalidOperationException("Response lost"); }
        return new(requestId, agentId, AgentRunStatus.Completed, "Result", null, 1000, 250, 1.25m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }
}
