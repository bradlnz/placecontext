using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Generate a defined report for a project: pull all its accumulated data, assemble the named
/// template's sections, derive an actionable plan, and (when an LLM is configured) polish the prose.
/// A command because it can optionally turn the action plan into work items.
/// </summary>
public sealed record GenerateReportCommand(
    Guid ProjectId,
    string? TemplateName = null,
    bool CreateWorkItems = false) : ICommand<ReportView>;
