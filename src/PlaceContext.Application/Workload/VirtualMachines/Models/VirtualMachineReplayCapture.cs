namespace PlaceContext.Application.Workload.VirtualMachines;

public sealed record VirtualMachineReplayCapture(VirtualMachineReplayFrame Frame, byte[]? Png);
