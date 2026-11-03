using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Jobs;

namespace PlaceContext.Application.Features;

public sealed class ReadJobSourceRecordsHandler(IDataSourceStore store, IJobDataAccess access)
    : IQueryHandler<ReadJobSourceRecordsQuery, IReadOnlyList<SourceRecord>>
{
    public async Task<IReadOnlyList<SourceRecord>> HandleAsync(ReadJobSourceRecordsQuery request, CancellationToken ct = default)
    {
        var projectId = access.RequireProjectId();
        if (request.Offset < 0 || request.Take is < 1 or > 100)
        {
            throw new ArgumentException("Use a non-negative offset and take from 1 to 100.");
        }
        var source = await store.GetAsync(projectId, request.SourceId, ct);
        return (await store.ReadDocumentsAsync(projectId, request.SourceId, ct)).Where(doc => DataSourcePipeline.IsRetrievable(source, doc)).OrderBy(doc => doc.Key, StringComparer.Ordinal)
            .Skip(request.Offset).Take(request.Take).Select(doc => new SourceRecord(doc.Key, doc.Json, doc.Review)).ToArray();
    }
}
