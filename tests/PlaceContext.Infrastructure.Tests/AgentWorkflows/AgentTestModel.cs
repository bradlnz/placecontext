using PlaceContext.Application.AgentWorkflows;
using PlaceContext.Application.Build;

namespace PlaceContext.Infrastructure.Tests.AgentWorkflows;

internal sealed class AgentTestModel : IModelConnections, IAgentModelGateway
{
    public int Calls;
    public AgentCompletion Completion { get; set; } = new("A proposed plan. Confirm the missing evidence before proceeding.", "test-model", 1000, 250);
    public TaskCompletionSource? Pause
    {
        get; set;
    }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<IReadOnlyList<ModelConnection>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ModelConnection>>([new(Guid.NewGuid(), "Test", "openai", "http://unused", ["test-model"], true, false)]);
    public async Task<AgentCompletion> CompleteMeteredAsync(Guid connectionId, string model, string instruction, string input, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        Started.TrySetResult();
        if (Pause is not null)
        {
            await Pause.Task.WaitAsync(ct);
        }
        return Completion;
    }
    public Task SaveAsync(SaveModelConnection connection, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> DiscoverAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
}
