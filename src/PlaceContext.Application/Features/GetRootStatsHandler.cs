using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetRootStatsHandler : IQueryHandler<GetRootStatsQuery, RootStatsView>
{
    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;
    private readonly IClock _clock;

    public GetRootStatsHandler(IProjectRepository projects, IActivityLogRepository ledgers, IClock clock)
    {
        _projects = projects;
        _ledgers = ledgers;
        _clock = clock;
    }

    public async Task<RootStatsView> HandleAsync(GetRootStatsQuery query, CancellationToken ct = default)
    {
        var projects = await _projects.ListAsync(ct);
        var since = _clock.UtcNow.Date;

        int agentToday = 0, humanToday = 0, godTotal = 0, stale = 0;
        var processScores = new List<double>();
        var technicalScores = new List<double>();

        foreach (var p in projects)
        {
            var ledger = await _ledgers.GetForProjectAsync(p.Id, ct);
            foreach (var r in ledger.Records.Where(r => r.RecordedAt >= since))
            {
                if (r.Author.IsAgent) agentToday++; else humanToday++;
            }

            godTotal += p.LastGraph?.GodNodes.Count ?? 0;
            if (RootRollup.IsStale(p, ledger)) stale++;
            if (p.ProcessRisk is not null) processScores.Add(p.ProcessRisk.Value);
            if (p.TechnicalRisk is not null) technicalScores.Add(p.TechnicalRisk.Value);
        }

        var process = processScores.Count > 0 ? processScores.Average() : 0.0;
        var technical = technicalScores.Count > 0 ? technicalScores.Average() : 0.0;

        return new RootStatsView(
            projects.Count, agentToday + humanToday, agentToday, humanToday,
            process, RootRollup.Band(process),
            technical, RootRollup.Band(technical),
            godTotal, stale);
    }
}
