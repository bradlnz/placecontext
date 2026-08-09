using PlaceContext.Domain.Entities;

namespace PlaceContext.Jobs.Integration;

public interface IJobDataClient
{
    Task ProcessJobResultAsync(Job job, JobRun run, CancellationToken cancellationToken = default);

    Task ProcessChainResultAsync(
        Guid chainId,
        Guid chainRunId,
        Guid projectId,
        string? primaryOutput,
        CancellationToken cancellationToken = default);
}
