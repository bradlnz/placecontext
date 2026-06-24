using System.Text;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SuggestImprovementsHandler : IQueryHandler<SuggestImprovementsQuery, ImprovementsView>
{
    private readonly IDecisionTreeProvider _tree;
    private readonly IChangeLedgerRepository _ledgers;
    private readonly IProjectContextRepository _contexts;
    private readonly IDebtAssessmentRepository _assessments;

    public SuggestImprovementsHandler(
        IDecisionTreeProvider tree, IChangeLedgerRepository ledgers,
        IProjectContextRepository contexts, IDebtAssessmentRepository assessments)
    {
        _tree = tree;
        _ledgers = ledgers;
        _contexts = contexts;
        _assessments = assessments;
    }

    public async Task<ImprovementsView> HandleAsync(SuggestImprovementsQuery query, CancellationToken ct = default)
    {
        var projectId = ProjectId.From(query.ProjectId);
        var tree = await _tree.BuildAsync(projectId, ct);
        var ledger = await _ledgers.GetForProjectAsync(projectId, ct);
        var context = await _contexts.GetForProjectAsync(projectId, ct);
        var debt = await _assessments.GetLatestAsync(projectId, ct);

        var items = new List<ImprovementView>();

        foreach (var hot in tree.Hotspots())
            items.Add(new ImprovementView(
                "churn-hotspot",
                hot.Degree >= 6 ? "high" : "medium",
                $"Churn hotspot: {hot.Label.Value}",
                $"Touched by {hot.Degree} changes. Consider refactoring or adding regression tests to stabilize it."));

        var unverified = ledger.Records.Count(r => r.IsAgentAuthored && !r.Verification.LiveVerified);
        if (unverified > 0)
            items.Add(new ImprovementView(
                "unverified-changes", unverified >= 3 ? "high" : "medium",
                $"{unverified} agent change(s) not live-verified",
                "Run and observe the app for these changes; record live verification on record_change."));

        var unreviewed = ledger.Records.Count(r => r.IsAgentAuthored && !r.Verification.ArchitectureReviewerRun);
        if (unreviewed > 0)
            items.Add(new ImprovementView(
                "unreviewed-changes", "low",
                $"{unreviewed} agent change(s) without an architecture review",
                "Run the architecture-reviewer on these slices to catch layer/SOLID regressions early."));

        if (context is null || context.IsEmpty)
            items.Add(new ImprovementView(
                "missing-context", "medium",
                "No project context recorded",
                "Capture goals, conventions, and gotchas with add_context so future sessions start informed."));

        if (debt is not null)
        {
            foreach (var s in debt.Signals.Where(s => (int)s.Severity >= 2).Take(3))
                items.Add(new ImprovementView(
                    $"debt:{s.Code}", "medium",
                    $"Debt signal: {s.Code}", s.Evidence));
        }

        if (items.Count == 0)
            items.Add(new ImprovementView("clean", "low", "No issues detected",
                "No hotspots, unverified changes, or debt signals from the logged activity."));

        return new ImprovementsView(query.ProjectId, items);
    }
}
