namespace PlaceContext.Application.Workload.VirtualMachines;

public sealed record VirtualMachineRunContext(Guid ProjectId, Guid RunId, int Step);
