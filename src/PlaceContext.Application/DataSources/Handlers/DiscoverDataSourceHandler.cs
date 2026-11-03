using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Jobs;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DiscoverDataSourceHandler(IDataSourceDiscovery discovery, IProjectRepository projects)
    : IQueryHandler<DiscoverDataSourceQuery, IReadOnlyList<SourceDataset>>
{
    public async Task<IReadOnlyList<SourceDataset>> HandleAsync(DiscoverDataSourceQuery request, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(projects, request.ProjectId, ct);
        SourceProviders.Get(request.Provider);
        return await discovery.DiscoverAsync(request.Provider, request.Connection, ct);
    }
}
