using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

/// <summary>Maps Domain aggregates to read-model views. Keeps mapping out of handlers and the host.</summary>
public static class ViewMapper
{
    public static ProjectSummaryView ToSummary(Project p) => new(
        p.Id.Value,
        p.Name.Value,
        p.Path.Value,
        p.Status.ToString(),
        p.IsGraphified,
        p.LastGraph?.GodNodes.Count ?? 0,
        p.LastGraph?.NodeCount ?? 0,
        p.LastGraph?.LinkCount ?? 0,
        p.TechnicalDebt?.Value,
        p.TechnicalDebt?.Band.ToString(),
        p.AgenticDebt?.Value,
        p.AgenticDebt?.Band.ToString());

    public static GodNodeView ToView(GodNode g) => new(g.Id.Value, g.Label.Value, g.Degree);

    public static DebtSignalView ToView(DebtSignal s) => new(s.Code, s.Severity.ToString(), s.Evidence);

    public static DebtDashboardView ToDashboard(DebtAssessment? a)
    {
        if (a is null) return DebtDashboardView.Empty;
        return new DebtDashboardView(
            a.Technical.Value, a.Technical.Band.ToString(),
            a.Agentic.Value, a.Agentic.Band.ToString(),
            a.TechnicalSignals.Select(ToView).ToList(),
            a.AgenticSignals.Select(ToView).ToList(),
            a.ComputedAt);
    }

    public static ChangeRecordView ToView(ChangeRecord r) => new(
        r.Id.Value,
        r.Sequence,
        string.IsNullOrWhiteSpace(r.Summary) ? "(no summary)" : r.Summary,
        r.Author.Name,
        r.Author.Kind.ToString(),
        r.Rationale.IsPresent ? r.Rationale.Value : "(none)",
        r.Commit?.Short,
        r.TestDelta.HasTestActivity,
        r.Verification.ArchitectureReviewerRun,
        r.Verification.LiveVerified,
        r.TouchedFiles,
        r.RecordedAt);

    public static DecisionView ToView(Decision d) => new(
        d.Id.Value, d.Question, d.Choice,
        d.Rationale.IsPresent ? d.Rationale.Value : "(none)", d.DecidedAt);

    public static ProjectContextView ToView(ProjectContext c) => new(
        c.ProjectId.Value, c.Markdown, c.IsEmpty, c.UpdatedAt);

    public static CodeRequirementsView ToView(CodeRequirements r) => new(
        r.ProjectId?.Value, r.IsGlobal, r.Markdown, r.IsEmpty, r.UpdatedAt);

    public static UsageEntryView ToView(UsageRecord r, decimal costUsd) => new(
        r.Id, r.Usage.Model, r.Usage.InputTokens, r.Usage.OutputTokens, costUsd, r.Description, r.RecordedAt);

    public static WorkItemView ToView(WorkItem w) => new(
        w.Id, w.ProjectId.Value, w.Title, w.Detail, w.Priority.ToString(), w.Status.ToString(),
        w.CreatedAt, w.ClaimedAt, w.CompletedAt);
}
