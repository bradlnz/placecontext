using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of tenant-defined report templates. Built-in defaults are not stored here.</summary>
public interface IReportTemplateRepository
{
    Task<IReadOnlyList<ReportTemplate>> ListAsync(CancellationToken ct = default);
    Task<ReportTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ReportTemplate?> GetByNameAsync(string name, CancellationToken ct = default);
    Task SaveAsync(ReportTemplate template, CancellationToken ct = default);
}
