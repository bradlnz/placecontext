using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;

namespace PlaceContext.Application.Features;

public sealed class ListReportTemplatesHandler : IQueryHandler<ListReportTemplatesQuery, IReadOnlyList<ReportTemplateView>>
{
    private readonly IReportTemplateRepository _templates;
    public ListReportTemplatesHandler(IReportTemplateRepository templates) => _templates = templates;

    public async Task<IReadOnlyList<ReportTemplateView>> HandleAsync(ListReportTemplatesQuery query, CancellationToken ct = default)
    {
        var stored = await _templates.ListAsync(ct);
        var storedNames = stored.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Built-ins first, then tenant-defined; a tenant template with the same name shadows the built-in.
        return BuiltInReportTemplates.All
            .Where(b => !storedNames.Contains(b.Name))
            .Concat(stored)
            .Select(ViewMapper.ToView)
            .ToList();
    }
}
