using PlaceContext.Application.Dtos;
using PlaceContext.Application;
namespace PlaceContext.Host.CoreApi;

public sealed class CoreApiResourceResolver : ICoreApiResourceResolver
{
    private readonly PlaceContextService _svc;
    public CoreApiResourceResolver(PlaceContextService svc) => _svc = svc;

    public Task<ProjectSummaryView?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
        => _svc.GetProjectByIdAsync(projectId, ct);

    public async Task<JobView?> GetJobAsync(Guid projectId, Guid jobId, CancellationToken ct = default)
    {
        var job = await _svc.GetJobAsync(jobId, ct);
        return job is null || job.ProjectId != projectId ? null : job;
    }
}
