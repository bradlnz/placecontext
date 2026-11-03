namespace PlaceContext.Application.Workload.VirtualMachines;

public sealed record VirtualMachineEnvironmentCatalog(bool Enabled, IReadOnlyList<VirtualMachineImageProfile> Images, bool AgentReady = false, string? AgentModel = null);
