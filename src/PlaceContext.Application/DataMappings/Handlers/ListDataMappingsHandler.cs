using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListDataMappingsHandler : IQueryHandler<ListDataMappingsQuery, IReadOnlyList<DataMappingView>>
{
    private readonly IDataMappingRepository _mappings;
    private readonly IJobRepository _jobs;

    public ListDataMappingsHandler(IDataMappingRepository mappings, IJobRepository jobs)
    {
        _mappings = mappings;
        _jobs = jobs;
    }

    public async Task<IReadOnlyList<DataMappingView>> HandleAsync(ListDataMappingsQuery query, CancellationToken ct = default)
    {
        var mappings = await _mappings.ListForProjectAsync(query.ProjectId, ct);
        var jobNames = (await _jobs.ListForProjectAsync(query.ProjectId, ct)).ToDictionary(j => j.Id, j => j.Name);
        return mappings
            .Select(m => DataMappingViewMapper.ToView(m, jobNames.GetValueOrDefault(m.JobId, "(deleted job)")))
            .ToList();
    }
}
