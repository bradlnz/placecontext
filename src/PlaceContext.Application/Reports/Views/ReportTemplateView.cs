namespace PlaceContext.Application.Dtos;

/// <summary>Read model: a defined report template (built-in or tenant-authored) and its sections.</summary>
public sealed record ReportTemplateView(
    Guid Id,
    string Name,
    string Description,
    bool IsBuiltIn,
    IReadOnlyList<ReportSectionView> Sections);

/// <summary>Read model: one section of a report template.</summary>
public sealed record ReportSectionView(string Title, string Source, string? Instruction);
