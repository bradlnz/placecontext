namespace PlaceContext.Application.Workload.VirtualMachines;

public interface IVirtualMachineReplayStore
{
    const int MaxFrames = 101;
    const int MaxImageBytes = 3_000_000;
    const int MaxReplayBytes = 32 * 1024 * 1024;
    Task AppendAsync(Guid runId, int step, VirtualMachineReplayCapture capture, CancellationToken ct);
    Task<IReadOnlyList<VirtualMachineReplayFrame>> ListAsync(Guid projectId, Guid runId, int step, CancellationToken ct);
    Task<byte[]?> ReadImageAsync(Guid projectId, Guid runId, int step, int sequence, CancellationToken ct);
}
