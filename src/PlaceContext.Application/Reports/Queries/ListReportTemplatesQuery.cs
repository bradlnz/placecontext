using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>List the available report templates — the built-in defaults plus any the tenant has defined.</summary>
public sealed record ListReportTemplatesQuery : IQuery<IReadOnlyList<ReportTemplateView>>;
