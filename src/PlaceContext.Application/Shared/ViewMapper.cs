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
        p.LastGraph?.LinkCount ?? 0);

    public static GodNodeView ToView(GodNode g) => new(g.Id.Value, g.Label.Value, g.Degree);

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

    public static RequirementsView ToView(Requirements r) => new(
        r.ProjectId?.Value, r.IsGlobal, r.Markdown, r.IsEmpty, r.UpdatedAt);

}
