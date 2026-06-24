using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetRootDebtHandler : IQueryHandler<GetRootDebtQuery, RootDebtView>
{
    private const int Window = 5;

    private readonly IProjectRepository _projects;
    private readonly IChangeLedgerRepository _ledgers;
    private readonly IDebtAssessmentRepository _assessments;
    private readonly AgenticDebtScorer _scorer;

    public GetRootDebtHandler(
        IProjectRepository projects, IChangeLedgerRepository ledgers,
        IDebtAssessmentRepository assessments, AgenticDebtScorer scorer)
    {
        _projects = projects;
        _ledgers = ledgers;
        _assessments = assessments;
        _scorer = scorer;
    }

    public async Task<RootDebtView> HandleAsync(GetRootDebtQuery query, CancellationToken ct = default)
    {
        var projects = await _projects.ListAsync(ct);

        var agentic = new List<double>();
        var technical = new List<double>();
        var agenticSignals = new List<DebtSignal>();
        var technicalSignals = new List<DebtSignal>();
        int agentChangesScored = 0, flagged = 0, godTotal = 0, stale = 0;

        foreach (var p in projects)
        {
            if (p.AgenticDebt is not null) agentic.Add(p.AgenticDebt.Value);
            if (p.TechnicalDebt is not null) technical.Add(p.TechnicalDebt.Value);
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
                agenticSignals.AddRange(sigs);
            }

            var latest = await _assessments.GetLatestAsync(p.Id, ct);
            if (latest is not null) technicalSignals.AddRange(latest.TechnicalSignals);
        }

        var trustBars = agenticSignals
            .GroupBy(s => s.Code)
            .Select(g => new { g.Key, Count = g.Count(), Sev = g.Max(x => x.Severity) })
            .OrderByDescending(x => x.Count)
            .Select(x => new TrustSignalBar(
                x.Key, SignalLabels.Label(x.Key), x.Count,
                agenticSignals.Count == 0 ? 0 : (int)Math.Round(100.0 * x.Count / agenticSignals.Count),
                SignalLabels.Tone(x.Sev)))
            .ToList();

        var techCards = BuildTechCards(technicalSignals, godTotal, projects.Count);

        var agAvg = agentic.Count > 0 ? agentic.Average() : 0.0;
        var techAvg = technical.Count > 0 ? technical.Average() : 0.0;

        return new RootDebtView(
            agAvg, RootRollup.Band(agAvg), techAvg, RootRollup.Band(techAvg),
            agentChangesScored, flagged, trustBars, techCards, stale);
    }

    private static IReadOnlyList<TechMetricCard> BuildTechCards(
        List<DebtSignal> technicalSignals, int godTotal, int projectCount)
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
