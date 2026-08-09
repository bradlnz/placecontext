using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SuggestImprovementsHandler
    : IQueryHandler<SuggestImprovementsQuery, ImprovementsView>
{
    private readonly IDecisionTreeProvider _tree;
    private readonly IActivityLogRepository _ledgers;

    public SuggestImprovementsHandler(
        IDecisionTreeProvider tree,
        IActivityLogRepository ledgers)
    {
        _tree = tree;
        _ledgers = ledgers;
    }

    public async Task<ImprovementsView> HandleAsync(
        SuggestImprovementsQuery query,
        CancellationToken ct = default)
    {
        var projectId = ProjectId.From(query.ProjectId);
        var tree = await _tree.BuildAsync(projectId, ct);
        var ledger = await _ledgers.GetForProjectAsync(projectId, ct);
        var items = new List<ImprovementView>();

        foreach (var hotspot in tree.Hotspots())
        {
            items.Add(new ImprovementView(
                "churn-hotspot",
                hotspot.Degree >= 6 ? "high" : "medium",
                $"Churn hotspot: {hotspot.Label.Value}",
                $"Touched by {hotspot.Degree} changes. Consider refactoring or adding regression tests to stabilize it."));
        }

        var unverified = ledger.Records.Count(record =>
            record.IsAgentAuthored && !record.Verification.LiveVerified);
        if (unverified > 0)
        {
            items.Add(new ImprovementView(
                "unverified-changes",
                unverified >= 3 ? "high" : "medium",
                $"{unverified} agent change(s) not live-verified",
                "Run and observe the app for these changes; record live verification on record_activity."));
        }

        var unreviewed = ledger.Records.Count(record =>
            record.IsAgentAuthored && !record.Verification.ArchitectureReviewerRun);
        if (unreviewed > 0)
        {
            items.Add(new ImprovementView(
                "unreviewed-changes",
                "low",
                $"{unreviewed} agent change(s) without an architecture review",
                "Run the architecture-reviewer on these slices to catch layer/SOLID regressions early."));
        }

        if (items.Count == 0)
        {
            items.Add(new ImprovementView(
                "clean",
                "low",
                "No issues detected",
                "No hotspots or unverified changes in the logged activity."));
        }

        return new ImprovementsView(query.ProjectId, items);
    }
}
