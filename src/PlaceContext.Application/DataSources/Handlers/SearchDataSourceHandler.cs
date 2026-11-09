using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed class SearchDataSourceHandler(DataSourcePipeline pipeline) : IQueryHandler<SearchDataSourceQuery, DataSourceSearchResult>
{
    public async Task<DataSourceSearchResult> HandleAsync(SearchDataSourceQuery request, CancellationToken ct = default)
    {
        return await pipeline.SearchAsync(request.ProjectId, request.SourceId, request.Query, request.Take, ct);
    }
}
