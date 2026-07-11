using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Translates domain entities to read model views for the Jobs feature.</summary>
internal static class JobViewMapper
{
    public static JobView ToView(Job job) => new(
        Id: job.Id,
        ProjectId: job.ProjectId,
        Name: job.Name,
        Description: job.Description,
        MapSourceKind: SourceKind(job.MapSpec.Source),
        MapImage: (job.MapSpec.Source as WorkloadSource.ImageWorkload)?.Image,
        MapRuntimeId: (job.MapSpec.Source as WorkloadSource.CodeWorkload)?.RuntimeId,
        MapSource: (job.MapSpec.Source as WorkloadSource.CodeWorkload)?.Source,
        MapEntrypoint: (job.MapSpec.Source as WorkloadSource.CodeWorkload)?.Entrypoint,
        MapFiles: Files(job.MapSpec.Source),
        ShardCount: job.MapSpec.ShardCount,
        InputPayloads: job.MapSpec.InputPayloads,
        MapEnv: job.MapSpec.Env,
        ReduceSourceKind: job.ReduceSpec is null ? null : SourceKind(job.ReduceSpec.Source),
        ReduceImage: (job.ReduceSpec?.Source as WorkloadSource.ImageWorkload)?.Image,
        ReduceRuntimeId: (job.ReduceSpec?.Source as WorkloadSource.CodeWorkload)?.RuntimeId,
        ReduceSource: (job.ReduceSpec?.Source as WorkloadSource.CodeWorkload)?.Source,
        ReduceEntrypoint: (job.ReduceSpec?.Source as WorkloadSource.CodeWorkload)?.Entrypoint,
        ReduceFiles: job.ReduceSpec is null ? Array.Empty<CodeFileDto>() : Files(job.ReduceSpec.Source),
        ReduceEnv: job.ReduceSpec?.Env,
        ConcurrencyLimit: job.ConcurrencyLimit,
        SuccessExitCodes: job.ExitCodePolicy.SuccessCodes.ToList(),
        PartialExitCodes: job.ExitCodePolicy.PartialCodes.ToList(),
        AllowNetworkEgress: job.AllowNetworkEgress,
        Parameters: job.Parameters.Select(p => new JobParameterDto(p.Name, p.Label, p.Required)).ToList(),
        PostJobActions: job.PostJobActions.ToList(),
        CreatedAt: job.CreatedAt,
        UpdatedAt: job.UpdatedAt);

    public static JobRunView ToSummaryView(JobRun run) => new(
        Id: run.Id,
        JobId: run.JobId,
        Status: run.Status.ToString(),
        StartedAt: run.StartedAt,
        FinishedAt: run.FinishedAt,
        ShardCount: run.ShardResults.Count,
        SucceededShards: run.ShardResults.Count(s => s.Outcome == WorkloadOutcome.Succeeded),
        PartialShards: run.ShardResults.Count(s => s.Outcome == WorkloadOutcome.Partial),
        FailedShards: run.ShardResults.Count(s => s.Outcome == WorkloadOutcome.Failed));

    public static JobRunDetailView ToDetailView(JobRun run) => new(
        Id: run.Id,
        JobId: run.JobId,
        ProjectId: run.ProjectId,
        Status: run.Status.ToString(),
        StartedAt: run.StartedAt,
        FinishedAt: run.FinishedAt,
        ShardResults: run.ShardResults
            .OrderBy(s => s.Index)
            .Select(s => new ShardResultView(s.Index, s.ExitCode, s.Outcome.ToString(), s.Artifact, s.Log,
                ToArtifactViews(s.Artifacts)))
            .ToList(),
        ReduceResult: run.ReduceResult is { } r
            ? new ReduceResultView(r.ExitCode, r.Succeeded, r.Artifact, r.Log, ToArtifactViews(r.Artifacts))
            : null,
        Snapshot: ToSnapshotView(run.Snapshot));

    private static IReadOnlyList<RunArtifactView> ToArtifactViews(IReadOnlyList<RunArtifact> artifacts)
        => artifacts.Select(a => new RunArtifactView(a.Name, a.Content, a.IsBinary)).ToList();

    private static JobRunSnapshotView ToSnapshotView(WorkloadSnapshot snap) => new(
        MapSourceKind: SourceKind(snap.MapSource),
        MapSourceLabel: snap.MapSource.Label,
        ReduceSourceKind: snap.ReduceSource is null ? null : SourceKind(snap.ReduceSource),
        ReduceSourceLabel: snap.ReduceSource?.Label,
        ConcurrencyLimit: snap.ConcurrencyLimit,
        ShardCount: snap.InputPayloads.Count,
        AllowNetworkEgress: snap.AllowNetworkEgress);

    private static IReadOnlyList<CodeFileDto> Files(WorkloadSource source) =>
        source is WorkloadSource.CodeWorkload code
            ? code.Files.Select(f => new CodeFileDto(f.Path, f.Content)).ToList()
            : Array.Empty<CodeFileDto>();

    private static string SourceKind(WorkloadSource source) => source switch
    {
        WorkloadSource.ImageWorkload => "image",
        WorkloadSource.CodeWorkload => "code",
        _ => "unknown",
    };
}
