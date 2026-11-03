namespace PlaceContext.Application.Workload.VirtualMachines;

public interface IVirtualMachineTaskAgent
{
    Task<IVirtualMachineTaskAgent> ForProjectAsync(Guid? projectId, CancellationToken ct) => Task.FromResult(this);
    string? ConfigurationError
    {
        get;
    }
    string Model
    {
        get;
    }
    Task<VirtualMachineEnvironmentResult> RunAsync(string prompt, string? input,
        Func<string, int, CancellationToken, Task<VirtualMachineEnvironmentResult>> executeGuest, CancellationToken ct,
        Func<VirtualMachineReplayCapture, CancellationToken, Task>? recordFrame = null, VirtualMachineAgentInteraction? interaction = null, Func<CancellationToken, Task<byte[]>>? readLiveFrame = null);
}
