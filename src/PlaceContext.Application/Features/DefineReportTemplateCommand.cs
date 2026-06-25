using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Define (or replace) a tenant report template. <paramref name="Sources"/> is an ordered list of
/// section source kinds (e.g. "Overview", "Risk", "ActionPlan"); each becomes a section titled by its
/// kind. Saving a template whose name matches an existing one replaces it.
/// </summary>
public sealed record DefineReportTemplateCommand(
    string Name,
    string Description,
    IReadOnlyList<string> Sources) : ICommand<ReportTemplateView>;
