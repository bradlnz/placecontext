using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record ReadJobSourceRecordsQuery(Guid SourceId, int Offset = 0, int Take = 50) : IQuery<IReadOnlyList<SourceRecord>>;
