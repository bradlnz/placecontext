using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record ReadSourceRecordsQuery(Guid ProjectId, Guid SourceId, int Offset = 0, int Take = 50) : IQuery<IReadOnlyList<SourceRecord>>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataRead;
}
