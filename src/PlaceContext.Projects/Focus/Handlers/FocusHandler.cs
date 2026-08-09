using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class FocusHandler : IQueryHandler<GetFocusQuery, FocusView>
{
    private static readonly Dictionary<string, int> SeverityRank = new()
    {
        ["high"] = 0,
        ["medium"] = 1,
        ["low"] = 2,
    };

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

        foreach (var project in projects)
        {
            var url = $"/project/{project.Id.Value}";
            var name = project.Name.Value;

            if (!project.IsGraphified)
            {
                items.Add(new FocusItem(
                    "graphify",
                    "low",
                    "Build the knowledge graph",
                    "Run rebuild_graph to map this project's structure.",
                    project.Id.Value,
                    name,
                    url));
            }

            var ledger = await _ledgers.GetForProjectAsync(project.Id, ct);
            var unverified = ledger.Records.Count(record =>
                record.IsAgentAuthored && !record.Verification.LiveVerified);
            if (unverified > 0)
            {
                items.Add(new FocusItem(
                    "unverified-changes",
                    unverified >= 3 ? "high" : "medium",
                    $"Verify {unverified} agent change(s)",
                    "Run and observe the app, then record live verification.",
                    project.Id.Value,
                    name,
                    $"{url}#changes"));
            }
        }

        var ordered = items
            .OrderBy(item => SeverityRank.GetValueOrDefault(item.Severity, 3))
            .ThenBy(item => item.Project, StringComparer.Ordinal)
            .Take(query.Limit)
            .ToList();

        return new FocusView(ordered, projects.Count);
    }
}
