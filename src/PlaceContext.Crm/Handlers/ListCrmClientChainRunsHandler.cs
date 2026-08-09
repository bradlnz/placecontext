using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Integration;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListCrmClientChainRunsHandler
    : IQueryHandler<ListCrmClientChainRunsQuery, IReadOnlyList<CrmChainRunView>>
{
    private readonly ICrmChainRunRepository _crmRuns;
    private readonly ICrmJobsClient _jobs;

    public ListCrmClientChainRunsHandler(
        ICrmChainRunRepository crmRuns,
        ICrmJobsClient jobs)
        => (_crmRuns, _jobs) = (crmRuns, jobs);

    public async Task<IReadOnlyList<CrmChainRunView>> HandleAsync(
        ListCrmClientChainRunsQuery query,
        CancellationToken ct = default)
    {
        var links = await _crmRuns.ListForClientAsync(query.ClientId, query.Take, ct);
        var views = new List<CrmChainRunView>(links.Count);
        foreach (var link in links)
        {
            var run = await _jobs.GetRunAsync(link.ChainRunId, ct);
            views.Add(new CrmChainRunView(
                link.Id,
                link.ClientId,
                link.ChainId,
                run?.ChainName ?? "Deleted job chain",
                link.ChainRunId,
                link.LifecycleStage.ToString(),
                run?.Status ?? "Unavailable",
                run?.StartedAt ?? link.StartedAt,
                run?.FinishedAt));
        }
        return views;
    }
}
