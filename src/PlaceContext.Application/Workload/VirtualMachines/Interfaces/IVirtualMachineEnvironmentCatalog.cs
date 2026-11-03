namespace PlaceContext.Application.Workload.VirtualMachines;

public interface IVirtualMachineEnvironmentCatalog
{
    VirtualMachineEnvironmentCatalog Get();
    Task<VirtualMachineEnvironmentCatalog> GetAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(Get());
}
