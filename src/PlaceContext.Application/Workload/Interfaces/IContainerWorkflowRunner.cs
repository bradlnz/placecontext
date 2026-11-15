using PlaceContext.Domain.ValueObjects;
namespace PlaceContext.Application.Workload;

public interface IContainerWorkflowRunner
{
    IAsyncDisposable TrackRun(Guid runId);
    Task<ContainerStepResult> ExecuteAsync(ChainAction action, Guid projectId, Guid runId, int step, string? payload, CancellationToken ct);
}
