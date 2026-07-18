using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Aggregates the workspace's actionable focus items across all projects — cheaply, from each project's
/// logged state (graph status and ledger) without rebuilding decision
/// trees. Items auto-resolve: they disappear from the checklist once the underlying condition clears.
/// </summary>
public sealed class FocusHandler : IQueryHandler<GetFocusQuery, FocusView>
{
    private static readonly Dictionary<string, int> SeverityRank = new() { ["high"] = 0, ["medium"] = 1, ["low"] = 2 };

    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;

    public FocusHandler(IProjectRepository projects, IActivityLogRepository ledgers)
    {
        _projects = projects;
        _ledgers = ledgers;
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

            var ledger = await _ledgers.GetForProjectAsync(p.Id, ct);
            var unverified = ledger.Records.Count(r => r.IsAgentAuthored && !r.Verification.LiveVerified);
            if (unverified > 0)
                items.Add(new FocusItem("unverified-changes", unverified >= 3 ? "high" : "medium",
                    $"Verify {unverified} agent change(s)",
                    "Run and observe the app, then record live verification.", p.Id.Value, name, $"{url}#changes"));
        }

        var ordered = items
            .OrderBy(i => SeverityRank.GetValueOrDefault(i.Severity, 3))
            .ThenBy(i => i.Project, StringComparer.Ordinal)
            .Take(query.Limit)
            .ToList();

        return new FocusView(ordered, projects.Count);
    }
}
