using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.CoreApi;

/// <summary>
/// Read-model lookup helper for Core API controllers. Keeps route-level guards in one place.
/// </summary>
public interface ICoreApiResourceResolver
{
    Task<ProjectSummaryView?> GetProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<JobView?> GetJobAsync(Guid projectId, Guid jobId, CancellationToken ct = default);
}
