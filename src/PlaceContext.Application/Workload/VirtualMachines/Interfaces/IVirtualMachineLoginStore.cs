namespace PlaceContext.Application.Workload.VirtualMachines;

public interface IVirtualMachineLoginStore
{
    Task<IReadOnlyList<VirtualMachineLoginProfile>> ListAsync(Guid projectId, CancellationToken ct = default);
    Task<byte[]> ReadAsync(Guid projectId, Guid profileId, string imageProfile, CancellationToken ct = default);
    Task SaveAsync(Guid projectId, string name, string imageProfile, byte[] profile, CancellationToken ct = default);
    Task DeleteAsync(Guid projectId, Guid profileId, CancellationToken ct = default);
}
