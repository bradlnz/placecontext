using PlaceContext.Application.Build;
using PlaceContext.Application.Workload;

namespace PlaceContext.Infrastructure.Workload;

public sealed class ReviewedWorkloadRunner(IWorkloadRunner runner, IOutcomeReviewer reviewer) : IWorkloadRunner
{
    public async Task<WorkloadRunResult> RunAsync(WorkloadRunRequest request, CancellationToken ct = default)
    {
        await ExecutionSafetyReview.RequireAsync(reviewer, new
        {
            kind = "job", request.Image, request.RuntimeId, request.Entrypoint,
            files = request.CodeFiles?.Select(file => new { file.Path, file.Content }),
            input = request.StdinPayload,
            mounts = request.ArtifactMounts.Select(file => new { file.ContainerPath, file.Content }),
            environmentNames = request.Env.Keys, request.AllowNetworkEgress, request.TimeoutSeconds,
            sandbox = "Unprivileged container; host mounts and privilege escalation are prohibited."
        }, request.ProjectId, ct);
        return await runner.RunAsync(request, ct);
    }

    public Task CancelAsync(string correlationId, CancellationToken ct = default) => runner.CancelAsync(correlationId, ct);
}
