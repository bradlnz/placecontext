using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Services;

/// <summary>
/// The code-defined report templates every tenant gets out of the box, so the generation layer works
/// before anyone authors a custom one. Industry-agnostic — sections pull from whatever a project has
/// accumulated. Stable ids let the portal link to them; they are never persisted.
/// </summary>
public static class BuiltInReportTemplates
{
    public const string OnboardingBriefName = "Onboarding Brief";

    public static IReadOnlyList<ReportTemplate> All { get; } = new[]
    {
        ReportTemplate.BuiltIn(
            new Guid("11111111-1111-1111-1111-111111111111"),
            OnboardingBriefName,
            "Pulls everything known about a project, organises it, and ends with an actionable plan — the fast way in.",
            new[]
            {
                new ReportSection("Overview", ReportSourceKind.Overview),
                new ReportSection("Context", ReportSourceKind.Context),
                new ReportSection("Requirements", ReportSourceKind.Requirements),
                new ReportSection("Key decisions", ReportSourceKind.Decisions),
                new ReportSection("Recent activity", ReportSourceKind.Activity),
                new ReportSection("Risk", ReportSourceKind.Risk),
                new ReportSection("Action plan", ReportSourceKind.ActionPlan,
                    "Prioritise the most important next steps; be concrete."),
            }),

        ReportTemplate.BuiltIn(
            new Guid("22222222-2222-2222-2222-222222222222"),
            "Project Status",
            "A stakeholder-facing snapshot: where the project stands, its risk, and what is in flight.",
            new[]
            {
                new ReportSection("Overview", ReportSourceKind.Overview),
                new ReportSection("Risk", ReportSourceKind.Risk),
                new ReportSection("Recent activity", ReportSourceKind.Activity),
                new ReportSection("Cost", ReportSourceKind.Usage),
            }),

        ReportTemplate.BuiltIn(
            new Guid("33333333-3333-3333-3333-333333333333"),
            "Action Plan",
            "Just the prioritised, actionable next steps derived from the project's logged state.",
            new[]
            {
                new ReportSection("Action plan", ReportSourceKind.ActionPlan,
                    "Prioritise the most important next steps; be concrete."),
            }),
    };

    public static ReportTemplate? ByName(string name)
        => All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
