using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Networking;

public interface IWorkerNetworkService
{
    Task<WorkerNetworkState> LoadAsync(Guid projectId, CancellationToken ct = default);
    Task SaveAsync(WorkerNetwork network, CancellationToken ct = default);
    Task DeleteAsync(Guid projectId, Guid networkId, long version, CancellationToken ct = default);
    Task AssignAsync(Guid projectId, Guid jobId, Guid? networkId, Guid? groupId, CancellationToken ct = default);
    Task<WorkerNetworkPolicy?> ResolveAsync(Guid projectId, Guid jobId, CancellationToken ct = default);
}
