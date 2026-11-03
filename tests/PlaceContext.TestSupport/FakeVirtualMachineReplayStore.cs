using PlaceContext.Application.Workload.VirtualMachines;
namespace PlaceContext.TestSupport;

public sealed class FakeVirtualMachineReplayStore : IVirtualMachineReplayStore
{
    public List<(Guid RunId, int Step, VirtualMachineReplayCapture Capture)> Captures { get; } = [];
    public Task AppendAsync(Guid runId, int step, VirtualMachineReplayCapture capture, CancellationToken ct)
    {
        Captures.Add((runId, step, capture));
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<VirtualMachineReplayFrame>> ListAsync(Guid projectId, Guid runId, int step, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VirtualMachineReplayFrame>>(Captures.Where(item => item.RunId == runId && item.Step == step).Select(item => item.Capture.Frame).ToArray());
    public Task<byte[]?> ReadImageAsync(Guid projectId, Guid runId, int step, int sequence, CancellationToken ct) =>
        Task.FromResult(Captures.FirstOrDefault(item => item.RunId == runId && item.Step == step && item.Capture.Frame.Sequence == sequence).Capture?.Png);
}
