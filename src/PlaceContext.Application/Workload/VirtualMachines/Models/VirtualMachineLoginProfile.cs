namespace PlaceContext.Application.Workload.VirtualMachines;

public sealed record VirtualMachineLoginProfile(Guid Id, string Name, string ImageProfile, DateTimeOffset UpdatedAt);
