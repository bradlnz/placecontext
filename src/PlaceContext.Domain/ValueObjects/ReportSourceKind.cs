namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Which slice of a project's accumulated data a report section is built from. Industry-agnostic:
/// the same kinds describe a software project, a compliance file, a clinical case, a campaign, etc.
/// </summary>
public enum ReportSourceKind
{
    Overview,
    Context,
    Requirements,
    Decisions,
    WorkItems,
    Activity,
    Risk,
    Usage,
    ActionPlan
}
