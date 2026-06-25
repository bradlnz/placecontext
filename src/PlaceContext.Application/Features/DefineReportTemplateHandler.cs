using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class DefineReportTemplateHandler : ICommandHandler<DefineReportTemplateCommand, ReportTemplateView>
{
    private readonly IReportTemplateRepository _templates;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public DefineReportTemplateHandler(IReportTemplateRepository templates, IUnitOfWork uow, IClock clock)
    {
        _templates = templates;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ReportTemplateView> HandleAsync(DefineReportTemplateCommand command, CancellationToken ct = default)
    {
        var sections = (command.Sources ?? Array.Empty<string>())
            .Select(s => Enum.TryParse<ReportSourceKind>(s?.Trim(), ignoreCase: true, out var kind) ? (ReportSourceKind?)kind : null)
            .Where(k => k is not null)
            .Select(k => new ReportSection(k!.ToString()!, k.Value))
            .ToList();

        if (sections.Count == 0)
            throw new InvalidOperationException(
                $"No valid section sources. Choose from: {string.Join(", ", Enum.GetNames<ReportSourceKind>())}.");

        var now = _clock.UtcNow;
        var existing = await _templates.GetByNameAsync(command.Name, ct);
        ReportTemplate template;
        if (existing is null)
        {
            template = ReportTemplate.Create(command.Name, command.Description, sections, now);
        }
        else
        {
            existing.Replace(command.Description, sections, now);
            template = existing;
        }

        await _templates.SaveAsync(template, ct);
        await _uow.SaveChangesAsync(ct);
        return ViewMapper.ToView(template);
    }
}
