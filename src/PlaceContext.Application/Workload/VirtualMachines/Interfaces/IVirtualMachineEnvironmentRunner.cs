using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Workload.VirtualMachines;

public interface IVirtualMachineEnvironmentRunner
{
    Task<VirtualMachineEnvironmentResult> RunAsync(VirtualMachineChainAction action, string? input, CancellationToken ct,
        Func<VirtualMachineReplayCapture, CancellationToken, Task>? recordFrame = null, VirtualMachineRunContext? context = null);
}
