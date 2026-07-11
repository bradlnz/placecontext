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
        p.TechnicalRisk?.Value,
        p.TechnicalRisk?.Band.ToString(),
        p.ProcessRisk?.Value,
        p.ProcessRisk?.Band.ToString());

    public static GodNodeView ToView(GodNode g) => new(g.Id.Value, g.Label.Value, g.Degree);

    public static RiskSignalView ToView(RiskSignal s) => new(s.Code, s.Severity.ToString(), s.Evidence);

    public static RiskDashboardView ToDashboard(RiskAssessment? a)
    {
        if (a is null) return RiskDashboardView.Empty;
        return new RiskDashboardView(
            a.Technical.Value, a.Technical.Band.ToString(),
            a.Process.Value, a.Process.Band.ToString(),
            a.TechnicalSignals.Select(ToView).ToList(),
            a.ProcessSignals.Select(ToView).ToList(),
            a.ComputedAt);
    }

    public static ActivityRecordView ToView(ActivityRecord r) => new(
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

    public static RequirementsView ToView(Requirements r) => new(
        r.ProjectId?.Value, r.IsGlobal, r.Markdown, r.IsEmpty, r.UpdatedAt);

    public static UsageEntryView ToView(UsageRecord r, decimal costUsd) => new(
        r.Id, r.Usage.Model, r.Usage.InputTokens, r.Usage.OutputTokens, costUsd, r.Description, r.RecordedAt);

    public static ReportTemplateView ToView(ReportTemplate t) => new(
        t.Id, t.Name, t.Description, t.IsBuiltIn,
        t.Sections.Select(s => new ReportSectionView(s.Title, s.Source.ToString(), s.Instruction)).ToList());
}
