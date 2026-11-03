using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class CreateSourceWorkflowHandler(IDataSourceStore sources, IJobRepository jobs,
    IJobChainRepository chains, IUnitOfWork uow, IClock clock, IPermissionService permissions)
    : ICommandHandler<CreateSourceWorkflowCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateSourceWorkflowCommand request, CancellationToken ct = default)
    {
        if (!await permissions.HasAsync(Permission.JobsEdit, ct) || !await permissions.HasAsync(Permission.DataWrite, ct))
        {
            throw new UnauthorizedAccessException("Creating a source workflow requires jobs.edit and data.write.");
        }
        if (!Uri.TryCreate(request.HostUrl, UriKind.Absolute, out var host) || host.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(host.UserInfo) || !string.IsNullOrEmpty(host.Query) || !string.IsNullOrEmpty(host.Fragment))
        {
            throw new ArgumentException("Provide a worker-reachable HTTP(S) portal URL without credentials, query or fragment.");
        }
        var source = await sources.GetAsync(request.ProjectId, request.SourceId, ct);
        var env = new Dictionary<string, string>
        {
            ["PC_HOST"] = host.AbsoluteUri.TrimEnd('/'), ["PC_USE_PROJECT_DATA"] = "true", ["PC_SYNC_SOURCE_ID"] = source.Id.ToString()
        };
        var job = Job.Create(request.ProjectId, "Sync " + source.Name, "Read, reconcile, tag and prepare source retrieval.",
            new MapSpec(new WorkloadSource.CodeWorkload("python", DataSourceWorkflow.Script, "main.py"), ["{}"], env),
            null, 1, ExitCodePolicy.Default, clock.UtcNow, allowNetworkEgress: true);
        var chain = JobChain.Create(request.ProjectId, source.Name + " data pipeline", "Source ingestion with stable provenance and retrieval chunks.", [job.Id], clock.UtcNow);
        await jobs.AddAsync(job, ct);
        await chains.AddAsync(chain, ct);
        await uow.SaveChangesAsync(ct);
        return chain.Id;
    }
}
