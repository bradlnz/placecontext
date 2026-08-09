using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Data.Integration;

namespace PlaceContext.Application.Features;

public sealed class ListDataMappingsHandler : IQueryHandler<ListDataMappingsQuery, IReadOnlyList<DataMappingView>>
{
    private readonly IDataMappingRepository _mappings;
    private readonly IDataJobsClient? _jobs;

    public ListDataMappingsHandler(
        IDataMappingRepository mappings,
        IDataJobsClient? jobs = null)
    {
        _mappings = mappings;
        _jobs = jobs;
    }

    public async Task<IReadOnlyList<DataMappingView>> HandleAsync(ListDataMappingsQuery query, CancellationToken ct = default)
    {
        var mappings = await _mappings.ListForProjectAsync(query.ProjectId, ct);
        var catalog = _jobs is null
            ? new DataJobCatalog([], [], [])
            : await _jobs.GetCatalogAsync(query.ProjectId, ct);
        var jobNames = catalog.Jobs.ToDictionary(j => j.Id, j => j.Name);
        var chainNames = catalog.Chains.ToDictionary(c => c.Id, c => c.Name);
        return mappings
            .Select(m => DataMappingViewMapper.ToView(m, m.SourceKind == "chain"
                ? chainNames.GetValueOrDefault(m.JobId, "(deleted chain)")
                : jobNames.GetValueOrDefault(m.JobId, "(deleted job)")))
            .ToList();
    }
}
