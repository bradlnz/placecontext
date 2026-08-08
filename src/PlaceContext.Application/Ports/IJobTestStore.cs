using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Ports;

/// <summary>Tenant-scoped persistence for job verification cases and their latest result.</summary>
public interface IJobTestStore
{
    Task<JobTestCaseRecord?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<JobTestCaseRecord>> ListForProjectAsync(
        Guid projectId, CancellationToken ct = default);
    Task SaveAsync(JobTestCaseRecord test, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
