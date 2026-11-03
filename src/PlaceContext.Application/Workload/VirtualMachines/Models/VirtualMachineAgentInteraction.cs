namespace PlaceContext.Application.Workload.VirtualMachines;

public sealed record VirtualMachineAgentInteraction(
    Func<CancellationToken, Task<string>> Checkpoint,
    Action RequestControl,
    Func<byte[]?, CancellationToken, Task> SaveProfile);
