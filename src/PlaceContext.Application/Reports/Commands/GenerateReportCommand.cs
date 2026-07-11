using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Generate a defined report for a project: pull all its accumulated data, assemble the named
/// template's sections, derive an actionable plan, and (when an LLM is configured) polish the prose.
/// </summary>
public sealed record GenerateReportCommand(
    Guid ProjectId,
    string? TemplateName = null) : ICommand<ReportView>;
