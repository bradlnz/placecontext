using System.Collections.Concurrent;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

/// <summary>In-memory store of tenant-defined report templates.</summary>
public sealed class InMemoryReportTemplateRepository : IReportTemplateRepository
{
    private readonly ConcurrentDictionary<Guid, ReportTemplate> _store = new();

    public Task<IReadOnlyList<ReportTemplate>> ListAsync(CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<ReportTemplate>)_store.Values.OrderBy(t => t.Name).ToList());

    public Task<ReportTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(id));

    public Task<ReportTemplate?> GetByNameAsync(string name, CancellationToken ct = default)
        => Task.FromResult(_store.Values.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task SaveAsync(ReportTemplate template, CancellationToken ct = default)
    {
        _store[template.Id] = template;
        return Task.CompletedTask;
    }
}
