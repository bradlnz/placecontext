using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed class ReadSourceRecordsHandler(IDataSourceStore store) : IQueryHandler<ReadSourceRecordsQuery, IReadOnlyList<SourceRecord>>
{
    public async Task<IReadOnlyList<SourceRecord>> HandleAsync(ReadSourceRecordsQuery request, CancellationToken ct = default)
    {
        if (request.Offset < 0 || request.Take is < 1 or > 100)
        {
            throw new ArgumentException("Use a non-negative offset and a page size of 1–100.");
        }
        var documents = await store.ReadDocumentsAsync(request.ProjectId, request.SourceId, ct);
        return documents.OrderBy(doc => doc.Key, StringComparer.Ordinal).Skip(request.Offset).Take(request.Take)
            .Select(doc => new SourceRecord(doc.Key, doc.Json, doc.Review, doc.ValidationError)).ToArray();
    }
}
