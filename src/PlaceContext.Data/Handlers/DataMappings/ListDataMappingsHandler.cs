using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListDataMappingsHandler : IQueryHandler<ListDataMappingsQuery, IReadOnlyList<DataMappingView>>
{
    private readonly IDataMappingRepository _mappings;
    private readonly IJobRepository? _jobs;
    private readonly IJobChainRepository? _chains;

    public ListDataMappingsHandler(
        IDataMappingRepository mappings,
        IJobRepository? jobs = null,
        IJobChainRepository? chains = null)
    {
        _mappings = mappings;
        _jobs = jobs;
        _chains = chains;
    }

    public async Task<IReadOnlyList<DataMappingView>> HandleAsync(ListDataMappingsQuery query, CancellationToken ct = default)
    {
        var mappings = await _mappings.ListForProjectAsync(query.ProjectId, ct);
        var jobNames = _jobs is null
            ? new Dictionary<Guid, string>()
            : (await _jobs.ListForProjectAsync(query.ProjectId, ct)).ToDictionary(j => j.Id, j => j.Name);
        var chainNames = _chains is null
            ? new Dictionary<Guid, string>()
            : (await _chains.ListForProjectAsync(query.ProjectId, ct)).ToDictionary(c => c.Id, c => c.Name);
        return mappings
            .Select(m => DataMappingViewMapper.ToView(m, m.SourceKind == "chain"
                ? chainNames.GetValueOrDefault(m.JobId, "(deleted chain)")
                : jobNames.GetValueOrDefault(m.JobId, "(deleted job)")))
            .ToList();
    }
}
