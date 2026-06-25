namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// One section of a defined report: a heading, the <see cref="ReportSourceKind"/> whose data fills
/// it, and an optional instruction that steers the (optional) LLM polish pass for this section.
/// </summary>
public sealed record ReportSection(string Title, ReportSourceKind Source, string? Instruction = null);
