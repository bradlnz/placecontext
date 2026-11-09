using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SearchProjectSourcesHandler(IDataSourceStore store, DataSourcePipeline pipeline, IProjectRepository projects)
    : IQueryHandler<SearchProjectSourcesQuery, IReadOnlyList<DataSourceSearchResult>>
{
    public async Task<IReadOnlyList<DataSourceSearchResult>> HandleAsync(SearchProjectSourcesQuery request, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(projects, request.ProjectId, ct);
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > 1000 || request.Take is < 1 or > 25)
        {
            throw new ArgumentException("Use a query of 1–1000 characters and a result limit of 1–25 per source.");
        }
        var results = new List<DataSourceSearchResult>();
        foreach (var source in await store.ListAsync(request.ProjectId, ct))
        {
            results.Add(await pipeline.SearchAsync(request.ProjectId, source.Id, request.Query, request.Take, ct));
        }
        return results;
    }
}
