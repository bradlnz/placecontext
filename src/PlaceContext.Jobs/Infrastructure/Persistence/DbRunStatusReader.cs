using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Jobs.Infrastructure.Persistence;

/// <summary>
/// Read side for the run-status watcher: compact projections of the tenant's in-flight runs and
/// the runs that recently reached a terminal status. Deliberately never selects the shard/artifact
/// JSON blobs — this runs on a short tick. Tenant isolation comes from the global query filters.
/// </summary>
public sealed class DbRunStatusReader : IRunStatusReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly JobsDbContext _db;

    public DbRunStatusReader(JobsDbContext db) => _db = db;

    public async Task<IReadOnlyList<JobRunStatusRow>> ListJobRunsAsync(
        DateTimeOffset finishedSince, CancellationToken ct = default)
    {
        var rows = await (
                from r in _db.JobRuns.AsNoTracking()
                where r.Status == nameof(JobRunStatus.Running) || r.Status == nameof(JobRunStatus.Queued)
                    || (r.FinishedAt != null && r.FinishedAt >= finishedSince)
                join j in _db.Jobs.AsNoTracking() on r.JobId equals j.Id into jobs
                from j in jobs.DefaultIfEmpty()
                select new { r.Id, r.JobId, JobName = (string?)j.Name, r.ProjectId, r.Status, r.StartedAt, r.FinishedAt })
            .ToListAsync(ct);

        return rows.Select(r => new JobRunStatusRow(
                r.Id, r.JobId, r.JobName ?? "job", r.ProjectId,
                Enum.TryParse<JobRunStatus>(r.Status, out var s) ? s : JobRunStatus.Failed,
                r.StartedAt, r.FinishedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<ChainRunStatusRow>> ListChainRunsAsync(
        DateTimeOffset finishedSince, CancellationToken ct = default)
    {
        var rows = await _db.ChainRuns.AsNoTracking()
            .Where(r => r.Status == nameof(ChainRunStatus.Running)
                || (r.FinishedAt != null && r.FinishedAt >= finishedSince))
            .Select(r => new { r.Id, r.ChainId, r.ChainName, r.ProjectId, r.Status, r.StepsJson, r.StartedAt, r.FinishedAt })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var steps = ParseSteps(r.StepsJson);
            return new ChainRunStatusRow(
                r.Id, r.ChainId, r.ChainName, r.ProjectId,
                Enum.TryParse<ChainRunStatus>(r.Status, out var s) ? s : ChainRunStatus.Failed,
                TotalSteps: steps.Count,
                FinishedSteps: steps.Count(st => st.Status is not (nameof(ChainStepStatus.Pending)) and not (nameof(ChainStepStatus.Running))),
                CurrentStepName: steps.FirstOrDefault(st => st.Status == nameof(ChainStepStatus.Running))?.JobName,
                StepRunIds: steps.Where(st => st.RunId is { } id && id != Guid.Empty).Select(st => st.RunId!.Value).ToList(),
                r.StartedAt, r.FinishedAt);
        }).ToList();
    }

    private static List<ChainRunStepStatusJson> ParseSteps(string stepsJson)
    {
        try { return JsonSerializer.Deserialize<List<ChainRunStepStatusJson>>(stepsJson, Json) ?? new(); }
        catch { return new(); }
    }

}
