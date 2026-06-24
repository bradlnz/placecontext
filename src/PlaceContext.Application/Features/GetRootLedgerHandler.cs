using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetRootLedgerHandler : IQueryHandler<GetRootLedgerQuery, RootLedgerView>
{
    private const int Window = 5;

    private readonly IProjectRepository _projects;
    private readonly IChangeLedgerRepository _ledgers;
    private readonly AgenticDebtScorer _scorer;

    public GetRootLedgerHandler(IProjectRepository projects, IChangeLedgerRepository ledgers, AgenticDebtScorer scorer)
    {
        _projects = projects;
        _ledgers = ledgers;
        _scorer = scorer;
    }

    public async Task<RootLedgerView> HandleAsync(GetRootLedgerQuery query, CancellationToken ct = default)
    {
        var projects = await _projects.ListAsync(ct);
        var entries = new List<LedgerEntryView>();

        foreach (var p in projects)
        {
            var ledger = await _ledgers.GetForProjectAsync(p.Id, ct);
            var gods = p.LastGraph?.GodNodes ?? Array.Empty<GodNode>();

            foreach (var r in ledger.Records)
            {
                var signals = r.IsAgentAuthored
                    ? _scorer.Score(r, gods, ledger.TouchesWithin(r.TouchedNodes, Window, r.Sequence))
                        .Select(s => SignalLabels.Label(s.Code)).ToList()
                    : new List<string>();

                entries.Add(new LedgerEntryView(
                    r.Id.Value, r.Sequence, p.Name.Value,
                    r.Author.Name, r.Author.Kind.ToString(),
                    string.IsNullOrWhiteSpace(r.Summary) ? "(no summary)" : r.Summary,
                    r.Rationale.IsPresent ? r.Rationale.Value : "No rationale recorded for this change.",
                    r.Rationale.IsPresent,
                    FormatTestDelta(r.TestDelta),
                    r.DebtDelta.Net,
                    r.Commit?.Short,
                    r.TouchedFiles.Count,
                    r.TouchedFiles,
                    signals.Count == 0,
                    signals,
                    r.RecordedAt));
            }
        }

        var ordered = entries.OrderByDescending(e => e.RecordedAt).Take(query.Take).ToList();
        return new RootLedgerView(ordered);
    }

    private static string FormatTestDelta(TestDelta d)
        => d.HasTestActivity ? $"+{d.Added} / −{d.Removed}" : "none";
}
