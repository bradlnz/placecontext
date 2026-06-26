using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Runs a job's configured post-job actions after a run completes: builds each output (HTML report,
/// chart, CSV, raw files) from the run's artifacts, stores it in the object store, and records a
/// <see cref="RunArtifactLink"/> so the portal/TUI can surface it. Entirely best-effort — every action
/// is isolated so one failure never fails the run or blocks the others.
/// </summary>
public sealed class PostJobActionService
{
    private readonly IObjectStore _store;
    private readonly IRunArtifactLinkRepository _links;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public PostJobActionService(IObjectStore store, IRunArtifactLinkRepository links, IUnitOfWork uow, IClock clock)
    {
        _store = store;
        _links = links;
        _uow = uow;
        _clock = clock;
    }

    public async Task RunAsync(Job job, JobRun run, CancellationToken ct = default)
    {
        if (job.PostJobActions.Count == 0 || !_store.IsEnabled) return;

        var bucket = _store.ReportsBucket;
        var added = false;

        foreach (var action in job.PostJobActions)
        {
            try
            {
                switch (action)
                {
                    case PostJobActionKind.HtmlReport:
                        added |= await StoreAsync(job, run, action, PostJobArtifacts.HtmlReport(job, run), bucket, ct);
                        break;
                    case PostJobActionKind.Chart:
                        added |= await StoreAsync(job, run, action, PostJobArtifacts.Chart(job, run), bucket, ct);
                        break;
                    case PostJobActionKind.Csv:
                        added |= await StoreAsync(job, run, action, PostJobArtifacts.Csv(run), bucket, ct);
                        break;
                    case PostJobActionKind.RawBundle:
                        foreach (var f in PostJobArtifacts.RawBundle(run))
                            added |= await StoreAsync(job, run, action, f, bucket, ct);
                        break;
                }
            }
            catch
            {
                // Best-effort: a failing action (e.g. object store hiccup) must not fail the run.
            }
        }

        if (added) await _uow.SaveChangesAsync(ct);
    }

    private async Task<bool> StoreAsync(Job job, JobRun run, PostJobActionKind kind,
        PostJobArtifacts.BuiltFile file, string bucket, CancellationToken ct)
    {
        var key = $"runs/{run.Id:N}/{file.FileName}";
        await _store.PutAsync(bucket, key, file.Content, file.ContentType, ct);
        await _links.AddAsync(RunArtifactLink.Create(
            run.Id, job.Id, run.ProjectId, kind, file.Title, bucket, key,
            file.ContentType, file.Content.LongLength, _clock.UtcNow), ct);
        return true;
    }
}
