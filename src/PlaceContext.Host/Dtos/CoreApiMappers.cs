using System.Linq;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Api;

/// <summary>Project/job/run mappers for Core API contracts.</summary>
public static class CoreApiMapper
{
    public static CoreProjectResponse ToResponse(ProjectSummaryView v) => new(
        v.Id, v.Name, v.Path, v.Status, v.IsGraphified);

    public static CoreJobResponse ToResponse(JobView v) => new(
        v.Id,
        v.ProjectId,
        v.Name,
        v.Description,
        v.MapSourceKind,
        v.MapImage,
        v.MapRuntimeId,
        v.MapSource,
        v.MapEntrypoint,
        v.MapFiles.Select(f => new CoreCodeFile(f.Path, f.Content)).ToList(),
        v.InputPayloads,
        v.MapEnv,
        v.ReduceSourceKind,
        v.ReduceImage,
        v.ReduceRuntimeId,
        v.ReduceSource,
        v.ReduceEntrypoint,
        v.ReduceFiles.Select(f => new CoreCodeFile(f.Path, f.Content)).ToList(),
        v.ReduceEnv,
        v.ConcurrencyLimit,
        v.SuccessExitCodes,
        v.PartialExitCodes,
        v.AllowNetworkEgress,
        v.AllowApiInvocation,
        v.Parameters.Select(p => new CoreJobParameter(p.Name, p.Label, p.Required, p.Type, p.Options?.ToList())).ToList(),
        v.PostJobActions.Select(a => a.ToString()).ToList(),
        v.ReturnType.ToString(),
        v.ReturnFileName,
        v.RetryCount,
        v.RetryDelaySeconds,
        v.McpConnectionIds,
        v.CreatedAt,
        v.UpdatedAt);

    public static CoreJobSummaryResponse ToSummary(JobView v) => new(
        v.Id,
        v.ProjectId,
        v.Name,
        v.Description,
        v.MapSourceKind,
        v.ReturnType.ToString(),
        v.AllowApiInvocation,
        v.AllowNetworkEgress,
        v.UpdatedAt);

    public static CoreJobRunSummaryResponse ToResponse(JobRunView v) => new(
        v.Id,
        v.JobId,
        v.Status,
        v.StartedAt,
        v.FinishedAt,
        v.ShardCount,
        v.SucceededShards,
        v.PartialShards,
        v.FailedShards);

    public static CoreJobRunDetailResponse ToResponse(JobRunDetailView v) => new(
        v.Id,
        v.JobId,
        v.ProjectId,
        v.Status,
        v.StartedAt,
        v.FinishedAt,
        v.AttemptNumber,
        v.OriginalRunId,
        new CoreRunSnapshotResponse(
            v.Snapshot.MapSourceKind,
            v.Snapshot.MapSourceLabel,
            v.Snapshot.ReduceSourceKind,
            v.Snapshot.ReduceSourceLabel,
            v.Snapshot.ConcurrencyLimit,
            v.Snapshot.ShardCount,
            v.Snapshot.AllowNetworkEgress),
        v.ShardResults.Select(s => new CoreShardResult(
            s.Index,
            s.ExitCode,
            s.Outcome,
            s.Artifact,
            s.Log,
            s.Artifacts.Select(a => new CoreRunArtifact(a.Name, a.Content, a.IsBinary)).ToList())).ToList(),
        v.ReduceResult is null
            ? null
            : new CoreReduceResult(
                v.ReduceResult.ExitCode,
                v.ReduceResult.Succeeded,
                v.ReduceResult.Artifact,
                v.ReduceResult.Log,
                v.ReduceResult.Artifacts.Select(a => new CoreRunArtifact(a.Name, a.Content, a.IsBinary)).ToList()));
}
