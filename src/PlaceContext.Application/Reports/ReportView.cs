namespace PlaceContext.Application.Dtos;

/// <summary>Read model: a generated report — the assembled (and optionally LLM-polished) Markdown plus
/// the structured action plan and any work items created from it.</summary>
public sealed record ReportView(
    Guid ProjectId,
    string ProjectName,
    string TemplateName,
    string Markdown,
    IReadOnlyList<ReportActionView> Actions,
    bool GeneratedByLlm,
    IReadOnlyList<string> CreatedWorkItems,
    DateTimeOffset GeneratedAt);

/// <summary>Read model: one prioritised, actionable next step in a report's action plan.</summary>
public sealed record ReportActionView(string Severity, string Title, string Detail);
