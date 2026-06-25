using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetRootRiskHandler : IQueryHandler<GetRootRiskQuery, RootRiskView>
{
    private const int Window = 5;

    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;
    private readonly IRiskAssessmentRepository _assessments;
    private readonly ProcessRiskScorer _scorer;

    public GetRootRiskHandler(
        IProjectRepository projects, IActivityLogRepository ledgers,
        IRiskAssessmentRepository assessments, ProcessRiskScorer scorer)
    {
        _projects = projects;
        _ledgers = ledgers;
        _assessments = assessments;
        _scorer = scorer;
    }

    public async Task<RootRiskView> HandleAsync(GetRootRiskQuery query, CancellationToken ct = default)
    {
        var projects = await _projects.ListAsync(ct);

        var process = new List<double>();
        var technical = new List<double>();
        var processSignals = new List<RiskSignal>();
        var technicalSignals = new List<RiskSignal>();
        int agentChangesScored = 0, flagged = 0, godTotal = 0, stale = 0;

        foreach (var p in projects)
        {
            if (p.ProcessRisk is not null) process.Add(p.ProcessRisk.Value);
            if (p.TechnicalRisk is not null) technical.Add(p.TechnicalRisk.Value);
            godTotal += p.LastGraph?.GodNodes.Count ?? 0;

            var ledger = await _ledgers.GetForProjectAsync(p.Id, ct);
            if (RootRollup.IsStale(p, ledger)) stale++;

            var gods = p.LastGraph?.GodNodes ?? Array.Empty<GodNode>();
            foreach (var change in ledger.RecentWindow(Window).Where(c => c.IsAgentAuthored))
            {
                agentChangesScored++;
                var reTouched = ledger.TouchesWithin(change.TouchedNodes, Window, change.Sequence);
                var sigs = _scorer.Score(change, gods, reTouched);
                if (sigs.Count > 0) flagged++;
                processSignals.AddRange(sigs);
            }

            var latest = await _assessments.GetLatestAsync(p.Id, ct);
            if (latest is not null) technicalSignals.AddRange(latest.TechnicalSignals);
        }

        var trustBars = processSignals
            .GroupBy(s => s.Code)
            .Select(g => new { g.Key, Count = g.Count(), Sev = g.Max(x => x.Severity) })
            .OrderByDescending(x => x.Count)
            .Select(x => new TrustSignalBar(
                x.Key, SignalLabels.Label(x.Key), x.Count,
                processSignals.Count == 0 ? 0 : (int)Math.Round(100.0 * x.Count / processSignals.Count),
                SignalLabels.Tone(x.Sev)))
            .ToList();

        var techCards = BuildTechCards(technicalSignals, godTotal, projects.Count);

        var agAvg = process.Count > 0 ? process.Average() : 0.0;
        var techAvg = technical.Count > 0 ? technical.Average() : 0.0;

        return new RootRiskView(
            agAvg, RootRollup.Band(agAvg), techAvg, RootRollup.Band(techAvg),
            agentChangesScored, flagged, trustBars, techCards, stale);
    }

    private static IReadOnlyList<TechMetricCard> BuildTechCards(
        List<RiskSignal> technicalSignals, int godTotal, int projectCount)
    {
        int Count(string code) => technicalSignals.Count(s => s.Code == code);
        int Pct(int n) => projectCount == 0 ? 0 : Math.Min(100, (int)Math.Round(100.0 * n / projectCount));
        string ToneFor(int n) => n == 0 ? "good" : n >= projectCount ? "bad" : "warn";

        return new List<TechMetricCard>
        {
            new("God-nodes", godTotal.ToString(), "files", Math.Min(100, godTotal * 4),
                godTotal == 0 ? "good" : godTotal > 12 ? "bad" : "warn"),
            new("Low test coverage", Count("LOW_COVERAGE").ToString(), "projects", Pct(Count("LOW_COVERAGE")), ToneFor(Count("LOW_COVERAGE"))),
            new("TODO / FIXME density", Count("TODO_DENSITY").ToString(), "projects", Pct(Count("TODO_DENSITY")), ToneFor(Count("TODO_DENSITY"))),
            new("High complexity", Count("HIGH_COMPLEXITY").ToString(), "projects", Pct(Count("HIGH_COMPLEXITY")), ToneFor(Count("HIGH_COMPLEXITY"))),
            new("Weak coupling signal", Count("WEAK_COUPLING_SIGNAL").ToString(), "projects", Pct(Count("WEAK_COUPLING_SIGNAL")), ToneFor(Count("WEAK_COUPLING_SIGNAL")))
        };
    }
}
