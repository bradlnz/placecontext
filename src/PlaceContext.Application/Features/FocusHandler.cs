using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Aggregates the workspace's actionable focus items across all projects — cheaply, from each project's
/// logged state (graph status, context, ledger, latest risk assessment) without rebuilding decision
/// trees. Items auto-resolve: they disappear from the checklist once the underlying condition clears.
/// </summary>
public sealed class FocusHandler : IQueryHandler<GetFocusQuery, FocusView>
{
    private static readonly Dictionary<string, int> SeverityRank = new() { ["high"] = 0, ["medium"] = 1, ["low"] = 2 };

    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;
    private readonly IProjectContextRepository _contexts;
    private readonly IRiskAssessmentRepository _assessments;

    public FocusHandler(
        IProjectRepository projects, IActivityLogRepository ledgers,
        IProjectContextRepository contexts, IRiskAssessmentRepository assessments)
    {
        _projects = projects;
        _ledgers = ledgers;
        _contexts = contexts;
        _assessments = assessments;
    }

    public async Task<FocusView> HandleAsync(GetFocusQuery query, CancellationToken ct = default)
    {
        var projects = await _projects.ListAsync(ct);
        var items = new List<FocusItem>();

        foreach (var p in projects)
        {
            var url = $"/project/{p.Id.Value}";
            var name = p.Name.Value;

            if (!p.IsGraphified)
                items.Add(new FocusItem("graphify", "low", "Build the knowledge graph",
                    "Run rebuild_graph to map this project's structure.", p.Id.Value, name, url));

            var context = await _contexts.GetForProjectAsync(p.Id, ct);
            if (context is null || context.IsEmpty)
                items.Add(new FocusItem("missing-context", "medium", "Add project context",
                    "Capture goals, conventions, and gotchas with add_context.", p.Id.Value, name, $"{url}#context"));

            var ledger = await _ledgers.GetForProjectAsync(p.Id, ct);
            var unverified = ledger.Records.Count(r => r.IsAgentAuthored && !r.Verification.LiveVerified);
            if (unverified > 0)
                items.Add(new FocusItem("unverified-changes", unverified >= 3 ? "high" : "medium",
                    $"Verify {unverified} agent change(s)",
                    "Run and observe the app, then record live verification.", p.Id.Value, name, $"{url}#changes"));

            var risk = await _assessments.GetLatestAsync(p.Id, ct);
            var signal = risk?.Signals.Where(s => (int)s.Severity >= 2).OrderByDescending(s => (int)s.Severity).FirstOrDefault();
            if (signal is not null)
                items.Add(new FocusItem($"risk:{signal.Code}", "medium",
                    $"Risk signal: {signal.Code}", signal.Evidence, p.Id.Value, name, url));
        }

        var ordered = items
            .OrderBy(i => SeverityRank.GetValueOrDefault(i.Severity, 3))
            .ThenBy(i => i.Project, StringComparer.Ordinal)
            .Take(query.Limit)
            .ToList();

        return new FocusView(ordered, projects.Count);
    }
}
